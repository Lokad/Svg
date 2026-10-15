// Packed multiply blending adapts pinned SkRasterPipeline_opts.h.
// Copyright 2018, 2022 Google LLC. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Maps primitive distances into the already placed device source grid.</summary>
internal readonly record struct SvgFilterDeviceMap(double ScaleX, double ScaleY)
{
    internal SvgFilterRegionContext? Regions { get; init; }
}

internal enum SvgFilterBoundsKind : byte { Visual, StoredImage }

/// <summary>Retains one integer-offset source branch and its composed intermediate crop.</summary>
internal readonly record struct SvgOffsetFilterPlacement(double X, double Y, bool AlphaOnly, SvgPaintBounds Clip);

/// <summary>Places a filter source in the bounded parent bitmaps used for restoration.</summary>
internal readonly record struct SvgFilterRasterPlacement(SvgCpuBitmapOrigin SourceOrigin, SvgCpuRasterGrid Grid, SvgAnalyticClipBounds Clip);

/// <summary>Maps primitive coordinates to the bounded source grid, separately from Gaussian distances.</summary>
internal readonly record struct SvgFilterRegionContext(SvgAffineTransform Coordinates, SvgViewportLengthBases LengthBases, SvgPaintBounds FilterRegion)
{
    internal SvgPaintBounds? SourceBounds { get; init; }
    /// <summary>Places this cropped source grid in its destination bitmap for native image sampling.</summary>
    internal SvgFilterRasterPlacement? RasterPlacement { get; init; }
    /// <summary>Identifies restoration into another filter's source bitmap, where native source-in retains its input image subset.</summary>
    internal bool IntermediateOutput { get; init; }
}

/// <summary>Owns the graph output and optional unquantized final shader samples until composition.</summary>
internal readonly record struct SvgFilterResult(SvgConverter.RasterizedSurface Frame,
    SvgShaderPaint[]? Samples, RenderBudget.BudgetReservation? SampleReservation) : IDisposable
{
    internal byte[] Pixels => Frame.Pixels;
    internal long WorkUnits => Frame.WorkUnits;
    public void Dispose() { SampleReservation?.Dispose(); Frame.Dispose(); }
}

/// <summary>Evaluates an ordered graph on one bounded premultiplied grid.</summary>
/// <remarks>Inputs and resources are borrowed and immutable. Each output has an admitted surface owner;
/// all intermediates are released on success, failure or cancellation. The final owner transfers to
/// the caller. Work includes every copy, conversion, blur pass and composition. Calls are independent.</remarks>
internal static class SvgFilterPlayback
{
    /// <summary>Computes the SVG visual bounds used to seed a source layer's image shaders.</summary>
    /// <remarks>Source/result bounds remain separate from object bounds and pixel ownership. Temporary
    /// arrays share the conversion budget, release on every exit and charge bounded graph visits.</remarks>
    internal static SvgPaintBounds SourceLayerBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds source, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => SourceLayerBounds(resource, map, context, source, SvgFilterBoundsKind.Visual, budget, work, token);

    /// <summary>Propagates visual hints or integer stored-image bounds through the same finite graph.</summary>
    /// <remarks>Stored bounds retain native kernel outsets and materialization rounding independently
    /// of compact pixel storage. Resources are borrowed; temporary ownership, work and cancellation
    /// follow the visual-bounds overload.</remarks>
    internal static SvgPaintBounds SourceLayerBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds source, SvgFilterBoundsKind kind,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = resource.Primitives.Items.Length;
        if (count == 0) return source;
        using RenderBudget.BudgetReservation owner = budget.Reserve(2 * ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count), SvgMemoryCategory.Temporary);
        SvgPaintBounds[] regions = new SvgPaintBounds[count], bounds = new SvgPaintBounds[count];
        PrepareRegions(resource, regions, context, work, token);
        for (int index = 0; index < count; index++)
        {
            work.Consume(token);
            SvgFilterPrimitive primitive = resource.Primitives.Items[index];
            SvgPaintBounds input = primitive.Input.Index < 0 ? source : bounds[primitive.Input.Index];
            SvgPaintBounds output = input;
            if (primitive.Kind == SvgFilterPrimitiveKind.Flood) output = regions[index];
            else if (primitive.Kind == SvgFilterPrimitiveKind.Merge)
            {
                output = default;
                foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                {
                    work.Consume(token);
                    if (merged.Index < -2 || merged.Index >= index) throw new SvgException(SvgErrorCode.MalformedInput, "Filter source bounds require earlier merge inputs.");
                    output = Union(output, merged.Index < 0 ? source : bounds[merged.Index]);
                }
            }
            else if (primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend)
            {
                SvgPaintBounds second = primitive.SecondInput.Index < 0 ? source : bounds[primitive.SecondInput.Index];
                output = primitive.Composition == SvgFilterComposition.In ? Intersect(input, second) : Union(input, second);
            }
            else if (input.Right > input.Left && input.Bottom > input.Top)
            {
                bool shifted = primitive.Kind is SvgFilterPrimitiveKind.Offset or SvgFilterPrimitiveKind.DropShadow;
                float dx = shifted ? (float)(primitive.OffsetX * map.ScaleX) : 0, dy = shifted ? (float)(primitive.OffsetY * map.ScaleY) : 0;
                if (!float.IsFinite(dx) || !float.IsFinite(dy)) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source bounds exceed the coordinate range.");
                float rx = 3f * (float)Math.Min(532, Math.Abs(primitive.SigmaX * map.ScaleX));
                float ry = 3f * (float)Math.Min(532, Math.Abs(primitive.SigmaY * map.ScaleY));
                if (kind == SvgFilterBoundsKind.StoredImage) { rx = MathF.Ceiling(rx); ry = MathF.Ceiling(ry); }
                output = new(input.Left + dx - rx, input.Top + dy - ry, input.Right + dx + rx, input.Bottom + dy + ry);
                if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow) output = Union(input, output);
            }
            bounds[index] = kind == SvgFilterBoundsKind.StoredImage
                ? Intersect(RoundOut(output), RoundOut(regions[index])) : Intersect(output, regions[index]);
        }
        return kind == SvgFilterBoundsKind.StoredImage ? bounds[^1] : Union(source, bounds[^1]);

        static SvgPaintBounds RoundOut(SvgPaintBounds value)
            => new(MathF.Floor(value.Left), MathF.Floor(value.Top), MathF.Ceiling(value.Right), MathF.Ceiling(value.Bottom));

        static SvgPaintBounds Intersect(SvgPaintBounds first, SvgPaintBounds second)
            => new(MathF.Max(first.Left, second.Left), MathF.Max(first.Top, second.Top), MathF.Min(first.Right, second.Right), MathF.Min(first.Bottom, second.Bottom));
        static SvgPaintBounds Union(SvgPaintBounds first, SvgPaintBounds second)
            => first.Right <= first.Left || first.Bottom <= first.Top ? second
                : second.Right <= second.Left || second.Bottom <= second.Top ? first
                : new(MathF.Min(first.Left, second.Left), MathF.Min(first.Top, second.Top), MathF.Max(first.Right, second.Right), MathF.Max(first.Bottom, second.Bottom));
    }

    /// <summary>Back-propagates a bounded output through earlier local results to the dynamic source.</summary>
    /// <remarks>Resources are borrowed and immutable. Temporary request/region arrays share the conversion
    /// budget and release on every exit; all graph visits charge work and observe cancellation. Coordinates
    /// use the caller's device grid. No pixels or caller storage are changed.</remarks>
    internal static SvgPaintBounds RequiredSourceBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds requested, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = resource.Primitives.Items.Length;
        if (count == 0) return requested;
        using RenderBudget.BudgetReservation owner = budget.Reserve(2 * ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count), SvgMemoryCategory.Temporary);
        SvgPaintBounds[] regions = new SvgPaintBounds[count], requests = new SvgPaintBounds[count];
        PrepareRegions(resource, regions, context, work, token);
        requests[count - 1] = requested;
        SvgPaintBounds source = default;
        for (int index = count - 1; index >= 0; index--)
        {
            work.Consume(token);
            SvgFilterPrimitive primitive = resource.Primitives.Items[index];
            SvgPaintBounds pending = requests[index], region = regions[index];
            SvgPaintBounds output = new(MathF.Floor(MathF.Max(pending.Left, region.Left)), MathF.Floor(MathF.Max(pending.Top, region.Top)),
                MathF.Ceiling(MathF.Min(pending.Right, region.Right)), MathF.Ceiling(MathF.Min(pending.Bottom, region.Bottom)));
            if (pending.Right <= pending.Left || pending.Bottom <= pending.Top || output.Right <= output.Left || output.Bottom <= output.Top) continue;
            if (primitive.Kind == SvgFilterPrimitiveKind.Flood) continue;
            SvgPaintBounds input = output;
            if (primitive.Kind is SvgFilterPrimitiveKind.Offset or SvgFilterPrimitiveKind.DropShadow)
            {
                float dx = (float)(primitive.OffsetX * map.ScaleX), dy = (float)(primitive.OffsetY * map.ScaleY);
                if (!float.IsFinite(dx) || !float.IsFinite(dy)) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source offset exceeds the coordinate range.");
                input = new(MathF.Floor(input.Left - dx), MathF.Floor(input.Top - dy), MathF.Ceiling(input.Right - dx), MathF.Ceiling(input.Bottom - dy));
            }
            if (primitive.Kind is SvgFilterPrimitiveKind.GaussianBlur or SvgFilterPrimitiveKind.DropShadow)
            {
                float radiusX = MathF.Ceiling(3f * (float)Math.Min(532, Math.Abs(primitive.SigmaX * map.ScaleX)));
                float radiusY = MathF.Ceiling(3f * (float)Math.Min(532, Math.Abs(primitive.SigmaY * map.ScaleY)));
                input = new(input.Left - radiusX, input.Top - radiusY, input.Right + radiusX, input.Bottom + radiusY);
            }
            if (primitive.Kind == SvgFilterPrimitiveKind.Merge)
                foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount)) Add(merged, output);
            else
            {
                Add(primitive.Input, input);
                if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow) Add(primitive.Input, output);
                if (primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend) Add(primitive.SecondInput, output);
            }

            void Add(SvgFilterInput target, SvgPaintBounds bounds)
            {
                work.Consume(token);
                if (target.Index < 0) source = Union(source, bounds);
                else if (target.Index < index) requests[target.Index] = Union(requests[target.Index], bounds);
                else throw new SvgException(SvgErrorCode.MalformedInput, "Filter source requests must refer to earlier results.");
            }
        }
        return source;

        static SvgPaintBounds Union(SvgPaintBounds first, SvgPaintBounds second)
            => first.Right <= first.Left || first.Bottom <= first.Top ? second
                : new(MathF.Min(first.Left, second.Left), MathF.Min(first.Top, second.Top), MathF.Max(first.Right, second.Right), MathF.Max(first.Bottom, second.Bottom));
    }

    private static void PrepareRegions(SvgFilterResource resource, Span<SvgPaintBounds> regions,
        SvgFilterRegionContext context, ParserWorkCounter work, CancellationToken token)
    {
        for (int index = 0; index < resource.Primitives.Items.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgFilterPrimitive primitive = resource.Primitives.Items[index];
            if (primitive.Input.Index < -2 || primitive.Input.Index >= index
                || primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend
                    && (primitive.SecondInput.Index < -2 || primitive.SecondInput.Index >= index))
                throw new SvgException(SvgErrorCode.MalformedInput, "Filter inputs must refer to the source or earlier results.");
            SvgPaintBounds defaults = primitive.Input.Index >= 0 ? regions[primitive.Input.Index] : context.FilterRegion;
            if (primitive.Kind == SvgFilterPrimitiveKind.Flood) defaults = context.FilterRegion;
            if (primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend)
            {
                SvgPaintBounds secondRegion = primitive.SecondInput.Index >= 0
                    ? regions[primitive.SecondInput.Index] : context.FilterRegion;
                defaults = new(Math.Min(defaults.Left, secondRegion.Left), Math.Min(defaults.Top, secondRegion.Top),
                    Math.Max(defaults.Right, secondRegion.Right), Math.Max(defaults.Bottom, secondRegion.Bottom));
            }
            if (primitive.Kind == SvgFilterPrimitiveKind.Merge && primitive.MergeCount > 0)
            {
                bool first = true;
                foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                {
                    work.Consume(token);
                    if (merged.Index < 0) { defaults = context.FilterRegion; break; }
                    if ((uint)merged.Index >= (uint)index) throw new SvgException(SvgErrorCode.MalformedInput, "Filter merge input is outside its earlier results.");
                    SvgPaintBounds region = regions[merged.Index];
                    if (first) { defaults = region; first = false; }
                    else defaults = new(Math.Min(defaults.Left, region.Left), Math.Min(defaults.Top, region.Top),
                        Math.Max(defaults.Right, region.Right), Math.Max(defaults.Bottom, region.Bottom));
                }
            }
            regions[index] = ResolveRegion(primitive.Region, defaults, context, work, token);
        }

        static SvgPaintBounds ResolveRegion(SvgFilterPrimitiveRegion? authored, SvgPaintBounds defaults, SvgFilterRegionContext context, ParserWorkCounter work, CancellationToken token)
        {
            if (authored is not {} region) return defaults;
            for (int component = 0; component < 4; component++) work.Consume(token);
            double x = region.X is {} rx ? Position(rx, context.LengthBases.Width, context.Coordinates.A, context.Coordinates.E) : defaults.Left;
            double y = region.Y is {} ry ? Position(ry, context.LengthBases.Height, context.Coordinates.D, context.Coordinates.F) : defaults.Top;
            double authoredWidth = region.Width is {} rw ? Length(rw, context.LengthBases.Width) : defaults.Right - defaults.Left;
            double authoredHeight = region.Height is {} rh ? Length(rh, context.LengthBases.Height) : defaults.Bottom - defaults.Top;
            if (authoredWidth <= 0 || authoredHeight <= 0) return default;
            double w = region.Width is not null ? authoredWidth * context.Coordinates.A : authoredWidth;
            double h = region.Height is not null ? authoredHeight * context.Coordinates.D : authoredHeight;
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            if (Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue || Math.Abs(x + w) > float.MaxValue || Math.Abs(y + h) > float.MaxValue
                || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(x + w) || !double.IsFinite(y + h))
                throw new SvgException(SvgErrorCode.LimitExceeded, "Primitive region exceeds the finite coordinate range.");
            return new((float)Math.Max(x, context.FilterRegion.Left), (float)Math.Max(y, context.FilterRegion.Top),
                (float)Math.Min(x + w, context.FilterRegion.Right), (float)Math.Min(y + h, context.FilterRegion.Bottom));

            static double Position(SvgFilterLength length, double? basis, double scale, double offset) => Length(length, basis) * scale + offset;
            static double Length(SvgFilterLength length, double? basis) => length.Percentage
                ? length.Value * (basis ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Primitive percentages require known viewport extents.")) / 100 : length.Value;
        }

    }

    private enum CropRounding { PixelCenter, Outward }
    internal static SvgFilterResult Apply(ReadOnlySpan<byte> source, int width, int height,
        SvgFilterResource resource, SvgFilterDeviceMap map, RenderBudget budget, long availableWork, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > int.MaxValue / 4 || source.Length != pixels * 4)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Filter frame exceeds contiguous RGBA storage.");
        int bytes = source.Length;
        int count = resource.Primitives.Items.Length;
        using RenderBudget.BudgetReservation tableOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgConverter.RasterizedSurface>(count + 2)
            + 2 * ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count) + ParserCollectionMemory.GetArrayByteCount<bool>(count), SvgMemoryCategory.Surface);
        SvgConverter.RasterizedSurface[] outputs = new SvgConverter.RasterizedSurface[count + 2];
        SvgPaintBounds[] regions = new SvgPaintBounds[count];
        SvgPaintBounds[] imageBounds = new SvgPaintBounds[count];
        bool[] linear = new bool[count];
        long work = 0;
        bool returned = false;
        SvgShaderPaint[]? samples = null;
        RenderBudget.BudgetReservation? sampleOwner = null;
        int finalIndex = count == 0 ? 0 : count - 1;
        try
        {
            if (map.Regions is {} regionContext)
            {
                ParserWorkCounter regionWork = new(availableWork);
                PrepareRegions(resource, regions, regionContext, regionWork, token);
                work += regionWork.Used;
            }
            ReadOnlySpan<byte> graphic = source;
            // Regions crop evaluated outputs. Retain the painted source halo
            // so a blur can sample strokes outside the requested output region.
            if (count == 0)
            {
                Charge(bytes);
                outputs[0] = Allocate();
                token.ThrowIfCancellationRequested();
                source.CopyTo(outputs[0].Pixels);
            }
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgFilterPrimitive primitive = resource.Primitives.Items[index];
                // Flood supplies a CSS color without interpolation; its declared
                // interpolation property does not change the color's sRGB origin.
                SvgFilterColorSpace colorSpace = primitive.Kind == SvgFilterPrimitiveKind.Flood
                    ? SvgFilterColorSpace.Srgb : primitive.ColorSpace ?? resource.ColorSpace;
                bool inputLinear = primitive.Input.Index >= 0 && primitive.Input.Index < index && linear[primitive.Input.Index];
                ReadOnlySpan<byte> input = ReadInput(primitive.Input, index, graphic);
                outputs[index] = Allocate();
                Span<byte> output = outputs[index].Pixels;
                imageBounds[index] = new(0, 0, width, height);
                if (map.Regions is not null)
                {
                    imageBounds[index] = regions[index];
                    if (regions[index].Right <= regions[index].Left || regions[index].Bottom <= regions[index].Top) continue;
                }
                else if (primitive.Region is not null)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Primitive filter regions require a placed coordinate context.");
                double sigmaX = primitive.SigmaX * Math.Abs(map.ScaleX), sigmaY = primitive.SigmaY * Math.Abs(map.ScaleY);
                if (double.IsNaN(sigmaX) || double.IsNaN(sigmaY))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Filter deviations exceed the finite coordinate range.");
                // Native Gaussian mapping saturates at 532 device units. Clamp
                // before narrowing so finite authored values never become an
                // unclassified infinity in the internal deviation contract.
                if (primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur)
                { sigmaX = Math.Min(sigmaX, 532); sigmaY = Math.Min(sigmaY, 532); }
                else if (sigmaX > float.MaxValue || sigmaY > float.MaxValue)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "This filter primitive requires unimplemented deviation rescaling.");
                SvgGaussianDeviation deviation = new((float)sigmaX, (float)sigmaY);
                if (primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur)
                {
                    if (SvgGaussianBlur.IsIdentity(deviation))
                        imageBounds[index] = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index]
                            : map.Regions?.SourceBounds ?? new(0, 0, width, height);
                    if (deviation.Horizontal > 135 || deviation.Vertical > 135)
                    {
                        SvgPaintBounds inputBounds = primitive.Input.Index >= 0 ? regions[primitive.Input.Index]
                            : map.Regions?.SourceBounds ?? new(0, 0, width, height);
                        if (map.Regions is not null)
                        {
                            SvgPaintBounds requested = regions[index];
                            inputBounds = new(Math.Min(inputBounds.Left, Math.Max(0, requested.Left)),
                                Math.Min(inputBounds.Top, Math.Max(0, requested.Top)),
                                Math.Max(inputBounds.Right, Math.Min(width, requested.Right)),
                                Math.Max(inputBounds.Bottom, Math.Min(height, requested.Bottom)));
                        }
                        // Only the final result is restored to the parent bitmap.
                        // Earlier nodes retain pixels that later offsets may bring onscreen.
                        SvgFilterRasterPlacement placement = (index == finalIndex ? map.Regions?.RasterPlacement : null)
                            ?? new(new(0, 0), new(width, height, true), new(0, 0, width, height));
                        work += SvgGaussianBlur.ApplyRescaled(input, output, width, height, deviation, inputBounds, regions[index], placement,
                            inputLinear, colorSpace, index == finalIndex && colorSpace == SvgFilterColorSpace.LinearRgb ? AllocateSamples() : Span<SvgShaderPaint>.Empty,
                            budget, availableWork - work, token);
                        if (map.Regions is not null) Clip(output, regions[index], CropRounding.Outward);
                        continue;
                    }
                    // A CPU kernel consumes stored working-space pixels; shader
                    // composition below keeps transfers unquantized until blending.
                    using SvgConverter.RasterizedSurface? srgbInput = inputLinear && colorSpace == SvgFilterColorSpace.Srgb ? Allocate() : null;
                    if (srgbInput is {} convertedSrgb)
                    {
                        ConvertColor(input, convertedSrgb.Pixels, SvgFilterColorSpace.Srgb);
                        input = convertedSrgb.Pixels;
                        inputLinear = false;
                    }
                    if (colorSpace == SvgFilterColorSpace.LinearRgb && (deviation.Horizontal > .03f || deviation.Vertical > .03f))
                    {
                        if (inputLinear) linear[index] = true;
                        else if (HasColor(input))
                        {
                            if (primitive.Input == SvgFilterInput.SourceGraphic)
                            {
                                if (outputs[count + 1].Pixels is null)
                                {
                                    outputs[count + 1] = Allocate();
                                    ConvertColor(input, outputs[count + 1].Pixels, SvgFilterColorSpace.LinearRgb);
                                }
                                input = outputs[count + 1].Pixels;
                            }
                            else
                            {
                                using SvgConverter.RasterizedSurface converted = Allocate();
                                ConvertColor(input, converted.Pixels, SvgFilterColorSpace.LinearRgb);
                                work += SvgGaussianBlur.Apply(converted.Pixels, output, width, height, deviation, budget, availableWork - work, token);
                                linear[index] = true;
                                if (map.Regions is not null) Clip(output, regions[index], CropRounding.Outward);
                                continue;
                            }
                            linear[index] = true;
                        }
                    }
                    work += SvgGaussianBlur.Apply(input, output, width, height, deviation, budget, availableWork - work, token);
                    linear[index] |= inputLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Flood)
                {
                    const float byteScale = 1f / 255f;
                    float alpha = primitive.Flood.Alpha * byteScale * primitive.FloodOpacity;
                    SvgShaderPaint flood = new(primitive.Flood.Red * byteScale * alpha,
                        primitive.Flood.Green * byteScale * alpha, primitive.Flood.Blue * byteScale * alpha, alpha);
                    Charge(pixels);
                    for (int offset = 0; offset < bytes; offset += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        flood.Blend(output.Slice(offset, 4), 255);
                    }
                    linear[index] = false;
                }
                else if (primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend)
                {
                    ReadOnlySpan<byte> second = ReadInput(primitive.SecondInput, index, graphic);
                    bool secondLinear = primitive.SecondInput.Index >= 0 && linear[primitive.SecondInput.Index];
                    bool destinationLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
                    ComposeInputs(input, second, output, inputLinear, secondLinear, primitive.Composition, colorSpace);
                    linear[index] = destinationLinear;
                    if (primitive.Composition == SvgFilterComposition.In && map.Regions is { IntermediateOutput: true } intermediate)
                    {
                        // A source-in result drawn into another filter's source retains
                        // the material input subset. Its nearest-offset image boundary
                        // is antialiased independently of the sampled texels. The final
                        // device route retains its existing broader allocated region.
                        SvgPaintBounds firstBounds = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index]
                            : intermediate.SourceBounds ?? intermediate.FilterRegion;
                        SvgPaintBounds secondBounds = primitive.SecondInput.Index >= 0 ? imageBounds[primitive.SecondInput.Index]
                            : intermediate.SourceBounds ?? intermediate.FilterRegion;
                        SvgPaintBounds current = imageBounds[index];
                        imageBounds[index] = new(MathF.Max(current.Left, MathF.Max(firstBounds.Left, secondBounds.Left)), MathF.Max(current.Top, MathF.Max(firstBounds.Top, secondBounds.Top)),
                            MathF.Min(current.Right, MathF.Min(firstBounds.Right, secondBounds.Right)), MathF.Min(current.Bottom, MathF.Min(firstBounds.Bottom, secondBounds.Bottom)));
                    }
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Offset)
                {
                    SvgPaintBounds? inputBounds = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index] : map.Regions?.SourceBounds;
                    Offset(input, output, primitive.OffsetX * map.ScaleX, primitive.OffsetY * map.ScaleY, inputBounds, null);
                    linear[index] = inputLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Merge)
                {
                    if (primitive.MergeCount == 1)
                    {
                        SvgFilterInput merged = resource.MergeInputs.Items[primitive.MergeFirst];
                        Charge(bytes);
                        ReadInput(merged, index, graphic).CopyTo(output);
                        linear[index] = merged.Index >= 0 && linear[merged.Index];
                        if (map.Regions is not null) Clip(output, regions[index], CropRounding.Outward);
                        continue;
                    }
                    bool destinationLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
                    foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                    {
                        ReadOnlySpan<byte> layer = ReadInput(merged, index, graphic);
                        bool layerLinear = merged.Index >= 0 && linear[merged.Index];
                        Composite(layer, output, layerLinear, colorSpace);
                    }
                    linear[index] = destinationLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow)
                {
                    if (colorSpace == SvgFilterColorSpace.LinearRgb && (primitive.Flood.Red != 0 || primitive.Flood.Green != 0 || primitive.Flood.Blue != 0))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Colored linearRGB shadow conversion is not implemented.");
                    if (colorSpace == SvgFilterColorSpace.LinearRgb)
                    {
                        Charge(pixels);
                        for (int offset = 0; offset < bytes; offset += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            if (input[offset + 3] != 255 && (input[offset] != 0 || input[offset + 1] != 0 || input[offset + 2] != 0))
                                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Translucent colored linearRGB shadow composition is not implemented.");
                        }
                    }
                    using SvgConverter.RasterizedSurface alpha = Allocate();
                    Charge(pixels);
                    for (int offset = 0; offset < bytes; offset += 4) { token.ThrowIfCancellationRequested(); alpha.Pixels[offset + 3] = input[offset + 3]; }
                    using SvgConverter.RasterizedSurface blurred = Allocate();
                    work += SvgGaussianBlur.Apply(alpha.Pixels, blurred.Pixels, width, height, deviation, budget, availableWork - work, token);
                    double shadowX = primitive.OffsetX * map.ScaleX, shadowY = primitive.OffsetY * map.ScaleY;
                    bool fractionalShadow = shadowX != Math.Truncate(shadowX) || shadowY != Math.Truncate(shadowY);
                    if (!fractionalShadow)
                    {
                        Charge(pixels);
                        for (int offset = 0; offset < bytes; offset += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            int a = (int)MathF.Floor(blurred.Pixels[offset + 3] * primitive.FloodOpacity * (primitive.Flood.Alpha / 255f) + .5f);
                            blurred.Pixels[offset] = (byte)((primitive.Flood.Red * a + 127) / 255);
                            blurred.Pixels[offset + 1] = (byte)((primitive.Flood.Green * a + 127) / 255);
                            blurred.Pixels[offset + 2] = (byte)((primitive.Flood.Blue * a + 127) / 255);
                            blurred.Pixels[offset + 3] = (byte)a;
                        }
                    }
                    Offset(blurred.Pixels, output, shadowX, shadowY, null, fractionalShadow ? primitive : null);
                    bool convertSource = inputLinear && colorSpace == SvgFilterColorSpace.Srgb;
                    Charge(pixels * (convertSource ? 2 : 1));
                    for (int offset = 0; offset < bytes; offset += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        if (convertSource) ReadPaint(input, offset, inputLinear, colorSpace).Blend(output.Slice(offset, 4), 255);
                        else SvgConverter.BlendPremultipliedOver(output.Slice(offset, 4), input[offset], input[offset + 1], input[offset + 2], input[offset + 3]);
                    }
                    linear[index] = inputLinear && !convertSource;
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter primitive is not implemented.");
                if (map.Regions is not null) Clip(output, regions[index], CropRounding.Outward);
            }
            if (count > 0 && linear[finalIndex])
            {
                AllocateSamples();
                ConvertColor(outputs[finalIndex].Pixels, outputs[finalIndex].Pixels, SvgFilterColorSpace.Srgb);
            }
            if (samples is not null && map.Regions is not null)
            {
                SvgPaintBounds finalRegion = regions[finalIndex];
                Charge(pixels);
                for (int y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = 0; x < width; x++)
                        if (x < MathF.Floor(finalRegion.Left) || x >= MathF.Ceiling(finalRegion.Right)
                            || y < MathF.Floor(finalRegion.Top) || y >= MathF.Ceiling(finalRegion.Bottom)) samples[y * width + x] = default;
                }
            }
            token.ThrowIfCancellationRequested();
            returned = true;
            return new(outputs[finalIndex] with { WorkUnits = work }, samples, sampleOwner);
        }
        finally
        {
            if (!returned) sampleOwner?.Dispose();
            for (int index = 0; index < outputs.Length; index++)
                if ((!returned || index != finalIndex) && outputs[index].Pixels is not null) outputs[index].Dispose();
        }

        Span<SvgShaderPaint> AllocateSamples()
        {
            token.ThrowIfCancellationRequested();
            if (samples is not null) return samples;
            sampleOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgShaderPaint>((int)pixels), SvgMemoryCategory.Surface);
            samples = new SvgShaderPaint[(int)pixels];
            return samples;
        }

        SvgConverter.RasterizedSurface Allocate()
        {
            token.ThrowIfCancellationRequested();
            RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(bytes), SvgMemoryCategory.Surface);
            try { return new(width, height, new byte[bytes], owner, 0); }
            catch { owner.Dispose(); throw; }
        }

        ReadOnlySpan<byte> ReadInput(SvgFilterInput reference, int before, ReadOnlySpan<byte> graphic)
        {
            if (reference == SvgFilterInput.SourceGraphic) return graphic;
            if (reference == SvgFilterInput.SourceAlpha)
            {
                if (outputs[count].Pixels is null)
                {
                    Charge(pixels);
                    outputs[count] = Allocate();
                    for (int offset = 0; offset < bytes; offset += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        outputs[count].Pixels[offset + 3] = graphic[offset + 3];
                    }
                }
                return outputs[count].Pixels;
            }
            if ((uint)reference.Index < (uint)before) return outputs[reference.Index].Pixels;
            throw new SvgException(SvgErrorCode.MalformedInput, "Filter graph input is outside its earlier results.");
        }

        void Composite(ReadOnlySpan<byte> input, Span<byte> output, bool inputLinear, SvgFilterColorSpace colorSpace)
        {
            Charge(pixels * (inputLinear == (colorSpace == SvgFilterColorSpace.LinearRgb) ? 1 : 2));
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                // A merge draws stored inputs into a bitmap. Without a transfer,
                // native direct image draws retain packed source-over rounding.
                // Color-filter inputs still use their unquantized shader route.
                if (inputLinear == (colorSpace == SvgFilterColorSpace.LinearRgb))
                    SvgConverter.BlendPremultipliedOver(output.Slice(offset, 4), input[offset], input[offset + 1], input[offset + 2], input[offset + 3]);
                else ReadPaint(input, offset, inputLinear, colorSpace).Blend(output.Slice(offset, 4), 255);
            }
        }

        void ComposeInputs(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output,
            bool firstLinear, bool secondLinear, SvgFilterComposition composition, SvgFilterColorSpace colorSpace)
        {
            bool destinationLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
            Charge(pixels * (1 + (firstLinear == destinationLinear ? 0 : 1) + (secondLinear == destinationLinear ? 0 : 1)));
            const float byteScale = 1f / 255f;
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                byte sourceAlphaByte = first[offset + 3], destinationAlphaByte = second[offset + 3];
                float sourceAlpha = sourceAlphaByte * byteScale, destinationAlpha = destinationAlphaByte * byteScale;
                SvgShaderPaint firstPaint = ReadPaint(first, offset, firstLinear, colorSpace), secondPaint = ReadPaint(second, offset, secondLinear, colorSpace);
                SvgShaderPaint sample = new(Channel(first[offset], second[offset], firstPaint.Red, secondPaint.Red),
                    Channel(first[offset + 1], second[offset + 1], firstPaint.Green, secondPaint.Green),
                    Channel(first[offset + 2], second[offset + 2], firstPaint.Blue, secondPaint.Blue),
                    Channel(sourceAlphaByte, destinationAlphaByte, sourceAlpha, destinationAlpha));
                sample.Blend(output.Slice(offset, 4), 255);

                float Channel(byte source, byte destination, float s, float d)
                {
                    // Native x64 low-precision multiply rounds the combined
                    // numerator through its bounded /256 approximation.
                    if (composition == SvgFilterComposition.Multiply && !destinationLinear && !firstLinear && !secondLinear)
                        return ((source * (255 - destinationAlphaByte) + destination * (255 - sourceAlphaByte)
                            + source * destination + 255) >> 8) * byteScale;
                    return composition switch
                    {
                        SvgFilterComposition.In => s * destinationAlpha,
                        SvgFilterComposition.Multiply => MathF.FusedMultiplyAdd(s, d,
                            MathF.FusedMultiplyAdd(s, 1 - destinationAlpha, d * (1 - sourceAlpha))),
                        SvgFilterComposition.Screen => MathF.FusedMultiplyAdd(-s, d, s + d),
                        _ => MathF.FusedMultiplyAdd(d, 1 - sourceAlpha, s)
                    };
                }
            }
        }

        static SvgShaderPaint ReadPaint(ReadOnlySpan<byte> input, int offset, bool linear, SvgFilterColorSpace destination)
        {
            const float byteScale = 1f / 255f;
            SvgShaderPaint sample = new(input[offset] * byteScale, input[offset + 1] * byteScale,
                input[offset + 2] * byteScale, input[offset + 3] * byteScale);
            return linear == (destination == SvgFilterColorSpace.LinearRgb) ? sample : SvgFilterColorTransfer.Convert(sample, destination);
        }

        void Charge(long amount)
        {
            if (amount > availableWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter graph exceeds the configured work limit.");
            work += amount;
        }

        bool HasColor(ReadOnlySpan<byte> input)
        {
            Charge(pixels);
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                if (input[offset] != 0 || input[offset + 1] != 0 || input[offset + 2] != 0) return true;
            }
            return false;
        }

        void ConvertColor(ReadOnlySpan<byte> input, Span<byte> output, SvgFilterColorSpace destination)
        {
            Charge(pixels);
            const float byteScale = 1f / 255f;
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                SvgShaderPaint sample = SvgFilterColorTransfer.Convert(new(input[offset] * byteScale,
                    input[offset + 1] * byteScale, input[offset + 2] * byteScale, input[offset + 3] * byteScale), destination);
                if (destination == SvgFilterColorSpace.Srgb && samples is not null) samples[offset / 4] = sample;
                output[offset] = Quantize(sample.Red, input[offset + 3]);
                output[offset + 1] = Quantize(sample.Green, input[offset + 3]);
                output[offset + 2] = Quantize(sample.Blue, input[offset + 3]);
                output[offset + 3] = input[offset + 3];
            }
            static byte Quantize(float value, byte alpha) => (byte)Math.Clamp((int)MathF.Round(value * 255f, MidpointRounding.ToEven), 0, alpha);
        }


        void Clip(Span<byte> pixels, SvgPaintBounds region, CropRounding rounding)
        {
            int left = Bound(region.Left, width, rounding, false), right = Bound(region.Right, width, rounding, true);
            int top = Bound(region.Top, height, rounding, false), bottom = Bound(region.Bottom, height, rounding, true);
            Charge(bytes);
            for (int row = 0; row < height; row++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> scan = pixels.Slice(row * width * 4, width * 4);
                if (row < top || row >= bottom || right <= left) scan.Clear();
                else { scan[..(left * 4)].Clear(); scan[(right * 4)..].Clear(); }
            }
            static int Bound(float value, int maximum, CropRounding rounding, bool upper)
                => value <= 0 ? 0 : value >= maximum ? maximum : rounding == CropRounding.PixelCenter
                    ? (int)Math.Floor(value + .5) : upper ? (int)Math.Ceiling(value) : (int)Math.Floor(value);
        }

        void Offset(ReadOnlySpan<byte> input, Span<byte> output, double dx, double dy, SvgPaintBounds? imageBounds, SvgFilterPrimitive? shadow)
        {
            ValidateOffset(dx, dy);
            bool fractional = dx != Math.Truncate(dx) || dy != Math.Truncate(dy);
            if (fractional && shadow is {} tint)
            {
                // DropShadow uses a linearly sampled matrix translation, while
                // ordinary feOffset retains nearest-neighbor displacement.
                // The source-in color filter follows interpolation and retains
                // its independently quantized constant premultiplied lanes.
                float floodAlpha = tint.FloodOpacity * (tint.Flood.Alpha * (1f / 255f));
                SvgConstantShaderPaint flood = new(Quantize(tint.Flood.Red * floodAlpha), Quantize(tint.Flood.Green * floodAlpha),
                    Quantize(tint.Flood.Blue * floodAlpha), Quantize(255 * floodAlpha));
                Charge(pixels * 4);
                for (int y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    long qy = (long)MathF.Floor(65536f * (y + .5f - (float)dy) + .5f) - 32768;
                    long top = qy >> 16;
                    int fractionY = unchecked((short)(qy ^ 0x8000));
                    for (int x = 0; x < width; x++)
                    {
                        token.ThrowIfCancellationRequested();
                        long qx = (long)MathF.Floor(65536f * (x + .5f - (float)dx) + .5f) - 32768;
                        long left = qx >> 16;
                        int fractionX = unchecked((short)(qx ^ 0x8000));
                        int at = (y * width + x) * 4;
                        byte alpha = SvgConstantShaderPaint.InterpolatePipeline(Read(input, width, height, left, top),
                            Read(input, width, height, left + 1, top), Read(input, width, height, left, top + 1),
                            Read(input, width, height, left + 1, top + 1), fractionX, fractionY).Alpha;
                        output[at] = Modulate(flood.Red, alpha);
                        output[at + 1] = Modulate(flood.Green, alpha);
                        output[at + 2] = Modulate(flood.Blue, alpha);
                        output[at + 3] = Modulate(flood.Alpha, alpha);
                    }
                }
                return;

                static SvgConstantShaderPaint Read(ReadOnlySpan<byte> pixels, int width, int height, long x, long y)
                    => new(0, 0, 0, x >= 0 && x < width && y >= 0 && y < height ? pixels[(int)(y * width + x) * 4 + 3] : (byte)0);
                static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Floor(value + .5f), 0, 255);
                static byte Modulate(byte value, byte alpha) => (byte)((value * alpha + 127) / 255);
            }
            SvgPaintBounds bounds = imageBounds ?? new(0, 0, width, height);
            // The SVG filter scope retains the union of source and displaced
            // drawable bounds. Its transparent padding must stay in the image;
            // nearest sampling clamps to this capture, not to painted geometry.
            bounds = new(MathF.Min(bounds.Left, MathF.Floor(bounds.Left + (float)dx)),
                MathF.Min(bounds.Top, MathF.Floor(bounds.Top + (float)dy)),
                MathF.Max(bounds.Right, MathF.Ceiling(bounds.Right + (float)dx)),
                MathF.Max(bounds.Bottom, MathF.Ceiling(bounds.Bottom + (float)dy)));
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(bounds.Left + (float)dx,
                bounds.Top + (float)dy, bounds.Right + (float)dx, bounds.Bottom + (float)dy, width, height);
            int imageLeft = (int)MathF.Floor(Math.Clamp(bounds.Left, 0, width));
            int imageTop = (int)MathF.Floor(Math.Clamp(bounds.Top, 0, height));
            int imageRight = (int)MathF.Ceiling(Math.Clamp(bounds.Right, 0, width));
            int imageBottom = (int)MathF.Ceiling(Math.Clamp(bounds.Bottom, 0, height));
            if (imageRight <= imageLeft || imageBottom <= imageTop) return;
            if (fractional) Charge(16);
            // SkImageFilters::Offset uses nearest sampling, retaining the legacy
            // layer-space pixel displacement rather than bilinear interpolation.
            if (dx != Math.Truncate(dx)) dx = Math.Floor((float)dx + .5f);
            if (dy != Math.Truncate(dy)) dy = Math.Floor((float)dy + .5f);
            ValidateOffset(dx, dy);
            Charge(pixels);
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                long sy = (long)y - (int)dy;
                for (int x = 0; x < width; x++)
                {
                    token.ThrowIfCancellationRequested();
                    long sx = (long)x - (int)dx;
                    if (fractional)
                    {
                        byte alpha = coverage.GetAlpha(x, y);
                        if (alpha == 0) continue;
                        int sampleX = (int)Math.Clamp(sx, imageLeft, imageRight - 1);
                        int sampleY = (int)Math.Clamp(sy, imageTop, imageBottom - 1);
                        int at = (sampleY * width + sampleX) * 4;
                        Span<byte> pixel = output.Slice((y * width + x) * 4, 4);
                        // Stored-image AA uses packed alpha modulation before
                        // the enclosing opacity layer, retaining both roundings.
                        int scale = alpha + 1;
                        pixel[0] = (byte)(input[at] * scale >> 8);
                        pixel[1] = (byte)(input[at + 1] * scale >> 8);
                        pixel[2] = (byte)(input[at + 2] * scale >> 8);
                        pixel[3] = (byte)(input[at + 3] * scale >> 8);
                        continue;
                    }
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                        input.Slice((int)(sy * width + sx) * 4, 4).CopyTo(output.Slice((y * width + x) * 4, 4));
                }
            }
        }

        static void ValidateOffset(double dx, double dy)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx < int.MinValue || dx > int.MaxValue || dy < int.MinValue || dy > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Filter offset exceeds the numeric range.");
        }
    }
}
