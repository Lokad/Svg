// Packed multiply blending adapts pinned SkRasterPipeline_opts.h.
// Nearest sprite translation admission follows pinned SkMatrix.cpp.
// Copyright 2018, 2022 Google LLC. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Maps primitive distances into the already placed device source grid.</summary>
internal readonly record struct SvgFilterDeviceMap(double ScaleX, double ScaleY)
{
    internal SvgFilterRegionContext? Regions { get; init; }
}

/// <summary>Retains a borrowed bitmap's image transform and separate source/output bounds.</summary>
/// <remarks>Immutable metadata owns no pixel storage. The enclosing filter/surface owns bytes, work and cancellation.</remarks>
internal sealed record SvgFilterDeferredImage(SvgShaderMatrix ImageToSource, SvgPaintBounds SourceBounds, SvgPaintBounds Clip, SvgFilterImageSampling Sampling)
{
    /// <summary>Retains uniform working-space paint; visible input crops resolve it to stored lanes before a later transform.</summary>
    internal SvgShaderPaint? Constant { get; init; }
    /// <summary>Retains a pending source color filter independently of the stored pixel color space.</summary>
    internal SvgFilterDeferredColorFilter ColorFilter { get; init; }
}

internal enum SvgFilterDeferredColorFilter : byte { None, SourceAlpha, LinearRgbRoundTrip, UniformFill }

internal enum SvgFilterImageSampling : byte { Nearest, Linear }

internal enum SvgFilterBoundsKind : byte { Visual, StoredImage, Output }

/// <summary>Retains one integer-offset source branch and its composed intermediate crop.</summary>
internal readonly record struct SvgOffsetFilterPlacement(double X, double Y, bool AlphaOnly, SvgPaintBounds Clip);

/// <summary>Borrows native parameter coordinates and the source bitmap origin in the playback grid.</summary>
/// <remarks>Immutable values own no storage. The caller keeps bitmap storage separately owned and checks cancellation during composition.</remarks>
internal readonly record struct SvgFilterNativeMapping(SvgShaderMatrix Coordinates, SvgCpuBitmapOrigin Origin);

/// <summary>Places a filter source in the bounded parent bitmaps used for restoration.</summary>
internal readonly record struct SvgFilterRasterPlacement(SvgCpuBitmapOrigin SourceOrigin, SvgCpuRasterGrid Grid, SvgAnalyticClipBounds Clip);

/// <summary>Maps primitive coordinates to the bounded source grid, separately from Gaussian distances.</summary>
internal readonly record struct SvgFilterRegionContext(SvgAffineTransform Coordinates, SvgViewportLengthBases LengthBases, SvgPaintBounds FilterRegion)
{
    /// <summary>Retains the native source-image hint, including transparent image padding.</summary>
    internal SvgPaintBounds? SourceBounds { get; init; }
    /// <summary>Retains the captured source subset in the playback grid, separately from the native image hint and scratch allocation.</summary>
    /// <remarks>Manual playback contexts that omit this subset use SourceBounds. This metadata owns no storage.</remarks>
    internal SvgPaintBounds? CapturedSourceBounds { get; init; }

    /// <summary>Places this cropped source grid in its destination bitmap for native image sampling.</summary>
    internal SvgFilterRasterPlacement? RasterPlacement { get; init; }
    /// <summary>Leaves the terminal sampled image in its operating space for an affine consumer to transfer after sampling.</summary>
    internal bool DeferFinalColorTransfer { get; init; }
    /// <summary>Identifies picture-backed destinations, whose terminal sRGB source offsets use constant image paint.</summary>
    internal bool PictureOutput { get; init; }
    /// <summary>Retains native matrix arithmetic for image transforms after affine source capture.</summary>
    internal SvgFilterNativeMapping? NativeMapping { get; init; }
    /// <summary>Maps authored primitive regions into the padded filter grid independently of source-image transforms.</summary>
    internal SvgFilterNativeMapping? NativeRegionMapping { get; init; }
    /// <summary>Allows the terminal dynamic source image to retain its offset for an affine consumer.</summary>
    internal bool DeferFinalOffset { get; init; }
}

/// <summary>Retains a uniform sRGB filter result and its finite source-grid coverage without owning storage.</summary>
internal readonly record struct SvgFilterUniformPaint(SvgShaderPaint Paint, SvgPaintBounds Bounds, bool HighPrecision)
{
    /// <summary>Retains terminal nearest-image edge coverage separately from packed uniform texels.</summary>
    internal SvgRectangleCoverage? Coverage { get; init; }
}

/// <summary>Owns the graph output and optional unquantized final shader samples until composition.</summary>
internal readonly record struct SvgFilterResult(SvgConverter.RasterizedSurface Frame,
    SvgShaderPaint[]? Samples, RenderBudget.BudgetReservation? SampleReservation) : IDisposable
{
    internal SvgFilterDeferredImage? DeferredImage { get; init; }
    internal SvgFilterUniformPaint? UniformPaint { get; init; }
    /// <summary>Retains a source color-filter stage through image-free aliases for later image sampling.</summary>
    internal SvgFilterDeferredColorFilter SamplingColorFilter => Frame.SamplingColorFilter;
    /// <summary>Identifies the stored pixel lanes; optional uniform paint metadata remains sRGB.</summary>
    internal SvgFilterColorSpace ColorSpace { get; init; } = SvgFilterColorSpace.Srgb;
    internal byte[] Pixels => Frame.Pixels;
    public void Dispose() { SampleReservation?.Dispose(); Frame.Dispose(); }
}

/// <summary>Evaluates an ordered graph on one bounded premultiplied grid.</summary>
/// <remarks>Inputs and resources are borrowed and immutable. Each output has an admitted surface owner;
/// all intermediates are released on success, failure or cancellation. The final owner transfers to
/// the caller. Work includes every copy, conversion, blur pass and composition. Calls are independent.</remarks>
internal static class SvgFilterPlayback
{
    // Native composition output is the input intersection for source-in and
    // the union for the supported over/multiply/screen operators. Callers charge
    // bounded setup work; this immutable helper owns no storage or cancellation.
    private static SvgPaintBounds CompositionBounds(SvgPaintBounds first, SvgPaintBounds second, SvgFilterComposition composition)
        => composition == SvgFilterComposition.In
            ? new(MathF.Max(first.Left, second.Left), MathF.Max(first.Top, second.Top), MathF.Min(first.Right, second.Right), MathF.Min(first.Bottom, second.Bottom))
            : first.Right <= first.Left || first.Bottom <= first.Top ? second
            : second.Right <= second.Left || second.Bottom <= second.Top ? first
            : new(MathF.Min(first.Left, second.Left), MathF.Min(first.Top, second.Top), MathF.Max(first.Right, second.Right), MathF.Max(first.Bottom, second.Bottom));

    private enum OffsetImagePaint { Packed, Constant, Floating, FloatingSamples, SourceAlpha }

    // Describes color stages independently of stored texels and image coverage.
    // Immutable, allocation-free metadata; the image draw owns work and cancellation.
    private readonly record struct ImageColorTransfer(SvgFilterColorSpace Source, SvgFilterColorSpace Working, SvgFilterColorSpace Destination);

    // A shader consumer samples the pending transform, without image-draw AA.
    // The source reference borrows an earlier owned graph result (or its dynamic
    // input) until Apply returns. This metadata owns no pixels or reservations.
    private readonly record struct SampledOffset(SvgFilterInput Input, SvgShaderMatrix InputToOutput, SvgPaintBounds Bounds, SvgPaintBounds Clip, SvgFilterColorSpace OperatingSpace);

    /// <summary>Tests origin alignment for a pure nearest-sampled translation on the native four-bit AA grid.</summary>
    /// <remarks>The caller admits matrix type, image extents and pixel coordinates separately and charges
    /// bounded setup work. This immutable predicate allocates nothing; nonfinite arithmetic is ineligible.</remarks>
    internal static bool IsNearestSpriteTranslation(float offset)
    {
        float subpixel = offset * 16;
        return float.IsFinite(offset) && float.IsFinite(subpixel)
            && MathF.Floor(subpixel + .5f) == MathF.Floor(offset + .5f) * 16;
    }

    /// <summary>Prepares a compact shadow's native unpremultiplied color and opacity bytes before premultiplication.</summary>
    /// <remarks>The validated CSS paint is sRGB; color conversion precedes byte admission. The result retains
    /// float precision for a pending input color filter. Callers choose its eventual packing, charge bounded
    /// setup work and own cancellation. This immutable operation allocates nothing and permits concurrent calls.</remarks>
    internal static SvgShaderPaint PrepareShadowShaderTint(SvgSolidPaint paint, SvgFilterColorSpace colorSpace)
    {
        const float byteScale = 1f / 255f;
        SvgShaderPaint color = new(paint.Color.Red * byteScale, paint.Color.Green * byteScale, paint.Color.Blue * byteScale, 1);
        if (colorSpace == SvgFilterColorSpace.LinearRgb) color = SvgFilterColorTransfer.Convert(color, colorSpace);
        float alpha = Quantize(paint.Alpha);
        return new(Quantize(color.Red) * alpha, Quantize(color.Green) * alpha, Quantize(color.Blue) * alpha, alpha);

        static float Quantize(float value) => MathF.Floor(value * 255 + .5f) * byteScale;
    }


    private readonly record struct UniformColor(bool Known, SvgShaderPaint Paint, bool HighPrecision);
    /// <summary>Encloses an authored source capture hint before mapping it to the filter layer.</summary>
    /// <remarks>Finite parameter-space inputs are caller-validated. Unlike layer rounding, this preserves
    /// every nonzero authored fringe. It allocates nothing.</remarks>
    internal static SvgPaintBounds RoundParameterBounds(SvgPaintBounds value)
        => new(MathF.Floor(value.Left), MathF.Floor(value.Top), MathF.Ceiling(value.Right), MathF.Ceiling(value.Bottom));

    /// <summary>Encloses the requested native source image independently of capture storage and transparent guard pixels.</summary>
    /// <remarks>Intersects the image hint and propagated request before integer admission; callers admit nonempty requests.
    /// This immutable helper allocates nothing; the caller owns cancellation.</remarks>
    internal static SvgPaintBounds RequestedSourceImageBounds(SvgPaintBounds hint, SvgPaintBounds needed)
    {
        double left = Math.Floor(MathF.Max(hint.Left, needed.Left)), top = Math.Floor(MathF.Max(hint.Top, needed.Top));
        double right = Math.Ceiling(MathF.Min(hint.Right, needed.Right)), bottom = Math.Ceiling(MathF.Min(hint.Bottom, needed.Bottom));
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom)
            || left < int.MinValue || left > int.MaxValue || top < int.MinValue || top > int.MaxValue
            || right < int.MinValue || right > int.MaxValue || bottom < int.MinValue || bottom > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source image exceeds the coordinate range.");
        return new((float)left, (float)top, (float)right, (float)bottom);
    }

    /// <summary>Rounds native layer bounds after the filter coordinate system's small integer-boundary inset.</summary>
    /// <remarks>Matches skif::RoundOut; this is coordinate rounding, not a pixel-comparison tolerance.
    /// The finite inputs are caller-validated.</remarks>
    internal static SvgPaintBounds RoundLayerBounds(SvgPaintBounds value)
    {
        const float roundEpsilon = 1e-3f;
        return new(MathF.Floor(value.Left + roundEpsilon), MathF.Floor(value.Top + roundEpsilon),
            MathF.Ceiling(value.Right - roundEpsilon), MathF.Ceiling(value.Bottom - roundEpsilon));
    }

    /// <summary>Computes the SVG visual bounds used to seed a source layer's image shaders.</summary>
    /// <remarks>Source/result bounds remain separate from object bounds and pixel ownership. Temporary
    /// arrays share the conversion budget, release on every exit and charge bounded graph visits.</remarks>
    internal static SvgPaintBounds SourceLayerBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds source, RenderBudget budget, CancellationToken token)
        => SourceLayerBounds(resource, map, context, source, SvgFilterBoundsKind.Visual, budget, token);

    /// <summary>Propagates capture hints, stored-image bounds or painted output through the same finite graph.</summary>
    /// <remarks>Stored bounds retain native kernel outsets and materialization rounding independently
    /// of compact pixel storage. Resources are borrowed; temporary ownership, work and cancellation
    /// follow the visual-bounds overload.</remarks>
    internal static SvgPaintBounds SourceLayerBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds source, SvgFilterBoundsKind kind,
        RenderBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = resource.Primitives.Items.Length;
        if (count == 0) return default;
        using RenderBudget.BudgetReservation owner = budget.Reserve(2 * ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count), SvgMemoryCategory.Temporary);
        SvgPaintBounds[] regions = new SvgPaintBounds[count], bounds = new SvgPaintBounds[count];
        PrepareRegions(resource, regions, context, token);
        PrepareOutputBounds(resource, map, source, kind, regions, bounds, token);
        return kind == SvgFilterBoundsKind.Visual ? Union(source, bounds[^1]) : bounds[^1];

        static SvgPaintBounds Union(SvgPaintBounds first, SvgPaintBounds second)
            => first.Right <= first.Left || first.Bottom <= first.Top ? second
                : second.Right <= second.Left || second.Bottom <= second.Top ? first
                : new(MathF.Min(first.Left, second.Left), MathF.Min(first.Top, second.Top), MathF.Max(first.Right, second.Right), MathF.Max(first.Bottom, second.Bottom));
    }

    // Propagates possible output bounds before request planning. The kind
    // retains its rounding domain; borrowed arrays and graph visits stay budgeted.
    private static void PrepareOutputBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgPaintBounds source, SvgFilterBoundsKind kind, ReadOnlySpan<SvgPaintBounds> regions,
        Span<SvgPaintBounds> bounds, CancellationToken token)
    {
        int count = resource.Primitives.Items.Length;
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgFilterPrimitive primitive = resource.Primitives.Items[index];
            SvgPaintBounds input = primitive.Input.Index < 0 ? source : bounds[primitive.Input.Index];
            SvgPaintBounds output = input;
            if (primitive.Kind == SvgFilterPrimitiveKind.Flood) output = regions[index];
            else if (primitive.Kind == SvgFilterPrimitiveKind.Merge)
            {
                output = default;
                foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                {
                    token.ThrowIfCancellationRequested();
                    if (merged.Index < -2 || merged.Index >= index) throw new SvgException(SvgErrorCode.MalformedInput, "Filter source bounds require earlier merge inputs.");
                    output = Union(output, merged.Index < 0 ? source : bounds[merged.Index]);
                }
            }
            else if (primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend)
            {
                SvgPaintBounds second = primitive.SecondInput.Index < 0 ? source : bounds[primitive.SecondInput.Index];
                output = CompositionBounds(input, second, primitive.Composition);
            }
            else if (input.Right > input.Left && input.Bottom > input.Top)
            {
                bool shifted = primitive.Kind is SvgFilterPrimitiveKind.Offset or SvgFilterPrimitiveKind.DropShadow;
                float dx = shifted ? (float)(primitive.OffsetX * map.ScaleX) : 0, dy = shifted ? (float)(primitive.OffsetY * map.ScaleY) : 0;
                if (!float.IsFinite(dx) || !float.IsFinite(dy)) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source bounds exceed the coordinate range.");
                float sigmaX = (float)Math.Min(532, Math.Abs(primitive.SigmaX * map.ScaleX));
                float sigmaY = (float)Math.Min(532, Math.Abs(primitive.SigmaY * map.ScaleY));
                // Native capture hints and stored image extents use separate
                // rounding, including materialized identity pixel kernels.
                float rx = 3f * sigmaX, ry = 3f * sigmaY;
                if (kind == SvgFilterBoundsKind.StoredImage)
                {
                    rx = sigmaX <= .03f ? 0 : MathF.Ceiling(rx);
                    ry = sigmaY <= .03f ? 0 : MathF.Ceiling(ry);
                }
                output = new(input.Left + dx - rx, input.Top + dy - ry, input.Right + dx + rx, input.Bottom + dy + ry);
                if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow) output = Union(input, output);
            }
            bounds[index] = kind == SvgFilterBoundsKind.StoredImage
                ? Intersect(RoundLayerBounds(output), RoundLayerBounds(regions[index])) : Intersect(output, regions[index]);
        }

        static SvgPaintBounds Intersect(SvgPaintBounds first, SvgPaintBounds second)
            => new(MathF.Max(first.Left, second.Left), MathF.Max(first.Top, second.Top), MathF.Min(first.Right, second.Right), MathF.Min(first.Bottom, second.Bottom));
        static SvgPaintBounds Union(SvgPaintBounds first, SvgPaintBounds second)
            => first.Right <= first.Left || first.Bottom <= first.Top ? second
                : second.Right <= second.Left || second.Bottom <= second.Top ? first
                : new(MathF.Min(first.Left, second.Left), MathF.Min(first.Top, second.Top), MathF.Max(first.Right, second.Right), MathF.Max(first.Bottom, second.Bottom));
    }

    /// <summary>Back-propagates a bounded output through earlier local results to the dynamic source.</summary>
    /// <remarks>Resources are borrowed and immutable. Temporary request/region arrays share the conversion
    /// budget and release on every exit; all graph visits observe cancellation. Coordinates
    /// use the caller's device grid. No pixels or caller storage are changed.</remarks>
    internal static SvgPaintBounds RequiredSourceBounds(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds requested, RenderBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = resource.Primitives.Items.Length;
        if (count == 0) return default;
        using RenderBudget.BudgetReservation owner = budget.Reserve(2 * ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count), SvgMemoryCategory.Temporary);
        SvgPaintBounds[] regions = new SvgPaintBounds[count], requests = new SvgPaintBounds[count];
        return PrepareRequests(resource, map, context, requested, regions, requests, Span<SvgPaintBounds>.Empty, token);
    }

    // Plans each node's requested output in the same grid as primitive regions.
    // Arrays are borrowed from the caller's budgeted scratch; graph work and
    // cancellation use the supplied counter. No pixel ownership is transferred.
    private static SvgPaintBounds PrepareRequests(SvgFilterResource resource, SvgFilterDeviceMap map,
        SvgFilterRegionContext context, SvgPaintBounds requested, SvgPaintBounds[] regions, SvgPaintBounds[] requests, Span<SvgPaintBounds> bounds,
        CancellationToken token)
    {
        int count = resource.Primitives.Items.Length;
        if (count == 0) return requested;
        PrepareRegions(resource, regions, context, token);
        if (context.SourceBounds is null) bounds = Span<SvgPaintBounds>.Empty;
        if (!bounds.IsEmpty && context.SourceBounds is {} content)
            PrepareOutputBounds(resource, map, content, SvgFilterBoundsKind.StoredImage, regions, bounds, token);
        requests[count - 1] = requested;
        SvgPaintBounds source = default;
        for (int index = count - 1; index >= 0; index--)
        {
            token.ThrowIfCancellationRequested();
            SvgFilterPrimitive primitive = resource.Primitives.Items[index];
            SvgPaintBounds pending = requests[index], region = regions[index];
            SvgPaintBounds output = new(MathF.Floor(MathF.Max(pending.Left, region.Left)), MathF.Floor(MathF.Max(pending.Top, region.Top)),
                MathF.Ceiling(MathF.Min(pending.Right, region.Right)), MathF.Ceiling(MathF.Min(pending.Bottom, region.Bottom)));
            if (!bounds.IsEmpty)
            {
                SvgPaintBounds possible = bounds[index];
                output = new(MathF.Max(output.Left, possible.Left), MathF.Max(output.Top, possible.Top),
                    MathF.Min(output.Right, possible.Right), MathF.Min(output.Bottom, possible.Bottom));
            }
            requests[index] = output;
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
                token.ThrowIfCancellationRequested();
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
        SvgFilterRegionContext context, CancellationToken token)
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
                    token.ThrowIfCancellationRequested();
                    if (merged.Index < 0) { defaults = context.FilterRegion; break; }
                    if ((uint)merged.Index >= (uint)index) throw new SvgException(SvgErrorCode.MalformedInput, "Filter merge input is outside its earlier results.");
                    SvgPaintBounds region = regions[merged.Index];
                    if (first) { defaults = region; first = false; }
                    else defaults = new(Math.Min(defaults.Left, region.Left), Math.Min(defaults.Top, region.Top),
                        Math.Max(defaults.Right, region.Right), Math.Max(defaults.Bottom, region.Bottom));
                }
            }
            regions[index] = ResolveRegion(primitive.Region, defaults, context, token);
        }

        static SvgPaintBounds ResolveRegion(SvgFilterPrimitiveRegion? authored, SvgPaintBounds defaults, SvgFilterRegionContext context, CancellationToken token)
        {
            if (authored is not {} region) return defaults;
            token.ThrowIfCancellationRequested();
            double x = region.X is {} rx ? Position(rx, context.LengthBases.Width, context.Coordinates.A, context.Coordinates.E, context.NativeRegionMapping, true) : defaults.Left;
            double y = region.Y is {} ry ? Position(ry, context.LengthBases.Height, context.Coordinates.D, context.Coordinates.F, context.NativeRegionMapping, false) : defaults.Top;
            double authoredWidth = region.Width is {} rw ? Length(rw, context.LengthBases.Width) : defaults.Right - defaults.Left;
            double authoredHeight = region.Height is {} rh ? Length(rh, context.LengthBases.Height) : defaults.Bottom - defaults.Top;
            if (authoredWidth <= 0 || authoredHeight <= 0) return default;
            double w = region.Width is not null ? context.NativeRegionMapping is {} mx
                ? (float)authoredWidth * mx.Coordinates.ScaleX : authoredWidth * context.Coordinates.A : authoredWidth;
            double h = region.Height is not null ? context.NativeRegionMapping is {} my
                ? (float)authoredHeight * my.Coordinates.ScaleY : authoredHeight * context.Coordinates.D : authoredHeight;
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            if (Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue || Math.Abs(x + w) > float.MaxValue || Math.Abs(y + h) > float.MaxValue
                || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(x + w) || !double.IsFinite(y + h))
                throw new SvgException(SvgErrorCode.LimitExceeded, "Primitive region exceeds the finite coordinate range.");
            return new((float)Math.Max(x, context.FilterRegion.Left), (float)Math.Max(y, context.FilterRegion.Top),
                (float)Math.Min(x + w, context.FilterRegion.Right), (float)Math.Min(y + h, context.FilterRegion.Bottom));

            static double Position(SvgFilterLength length, double? basis, double scale, double offset, SvgFilterNativeMapping? native, bool horizontal)
            {
                if (native is {} mapping)
                {
                    SvgShaderMatrix coordinates = mapping.Coordinates;
                    return horizontal ? MathF.FusedMultiplyAdd((float)Length(length, basis), coordinates.ScaleX, coordinates.TranslateX) + mapping.Origin.X
                        : MathF.FusedMultiplyAdd((float)Length(length, basis), coordinates.ScaleY, coordinates.TranslateY) + mapping.Origin.Y;
                }
                return Length(length, basis) * scale + offset;
            }
            static double Length(SvgFilterLength length, double? basis) => length.Percentage
                ? length.Value * (basis ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Primitive percentages require known viewport extents.")) / 100 : length.Value;
        }

    }

    // Evaluates the full available grid. Numerical mechanism callers use this
    // named convenience; rendering supplies its distinct requested output below.
    internal static SvgFilterResult ApplyFullFrame(ReadOnlySpan<byte> source, int width, int height,
        SvgFilterResource resource, SvgFilterDeviceMap map, RenderBudget budget, CancellationToken token)
        => Apply(source, width, height, resource, map, new(0, 0, width, height), budget, token);

    // Requested output uses the source grid's coordinates, independently of its
    // physical allocation and the source ink. All storage/work stays budgeted.
    internal static SvgFilterResult Apply(ReadOnlySpan<byte> source, int width, int height,
        SvgFilterResource resource, SvgFilterDeviceMap map, SvgPaintBounds requestedOutput, RenderBudget budget,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > int.MaxValue / 4 || source.Length != pixels * 4)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Filter frame exceeds contiguous RGBA storage.");
        int bytes = source.Length;
        int count = resource.Primitives.Items.Length;
        using RenderBudget.BudgetReservation tableOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgConverter.RasterizedSurface>(count + 2)
            + 3 * ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count) + ParserCollectionMemory.GetArrayByteCount(count, System.Runtime.CompilerServices.Unsafe.SizeOf<SampledOffset?>()) + ParserCollectionMemory.GetArrayByteCount<bool>(count) + ParserCollectionMemory.GetArrayByteCount<UniformColor>(count), SvgMemoryCategory.Surface);
        SvgConverter.RasterizedSurface[] outputs = new SvgConverter.RasterizedSurface[count + 2];
        SvgPaintBounds[] regions = new SvgPaintBounds[count];
        SvgPaintBounds[] imageBounds = new SvgPaintBounds[count];
        SvgPaintBounds[] requests = new SvgPaintBounds[count];
        SampledOffset?[] sampledOffsets = new SampledOffset?[count];
        bool[] linear = new bool[count];
        UniformColor[] uniform = new UniformColor[count];
        bool returned = false;
        SvgShaderPaint[]? samples = null;
        bool linearOffsetSamples = false;
        RenderBudget.BudgetReservation? sampleOwner = null;
        SvgFilterDeferredImage? deferredImage = null;
        SvgFilterUniformPaint? sampledUniform = null;
        int finalIndex = count == 0 ? 0 : count - 1;
        try
        {
            if (map.Regions is {} regionContext)
            {
                PrepareRequests(resource, map, regionContext, requestedOutput, regions, requests, imageBounds, token);
            }
            ReadOnlySpan<byte> graphic = source;
            // Regions crop evaluated outputs. Retain the painted source halo
            // so a blur can sample strokes outside the requested output region.
            if (count == 0)
            {
                token.ThrowIfCancellationRequested();
                outputs[0] = Allocate();
                token.ThrowIfCancellationRequested();
            }
            int finalPaintIndex = count == 0 ? -1 : ResolvePaintSource(new(finalIndex)).Index;
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgFilterPrimitive primitive = resource.Primitives.Items[index];
                bool nearestSampling = false;
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
                else if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow && ((float)sigmaX > 135 || (float)sigmaY > 135))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Drop-shadow deviations above 135 device pixels per axis are not implemented.");
                else if (sigmaX > float.MaxValue || sigmaY > float.MaxValue)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "This filter primitive requires unimplemented deviation rescaling.");
                SvgGaussianDeviation deviation = new((float)sigmaX, (float)sigmaY);
                if (primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur)
                {
                    float sampleRadiusX = MathF.Ceiling(3f * deviation.Horizontal), sampleRadiusY = MathF.Ceiling(3f * deviation.Vertical);
                    SvgPaintBounds wanted = requests[index];
                    SvgPaintBounds sampleDemand = new(wanted.Left - sampleRadiusX, wanted.Top - sampleRadiusY, wanted.Right + sampleRadiusX, wanted.Bottom + sampleRadiusY);
                    SvgShaderMatrix sampleInverse = default;
                    bool resolvesSample = (deviation.Horizontal > .03f || deviation.Vertical > .03f)
                        && TryPrepareSample(primitive.Input, sampleDemand, out sampleInverse);
                    using SvgConverter.RasterizedSurface? sampledInput = resolvesSample ? Allocate() : null;
                    if (sampledInput is {} resolved)
                    {
                        token.ThrowIfCancellationRequested();
                        bool floating = inputLinear != (colorSpace == SvgFilterColorSpace.LinearRgb) || IsAlpha(primitive.Input);
                        for (int offset = 0; offset < bytes; offset += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            SvgShaderPaint paint = Sample(primitive.Input, sampleInverse, offset, colorSpace, graphic);
                            if (floating) paint.Blend(resolved.Pixels.AsSpan(offset, 4), 255);
                            else paint.AsConstant(1f).Blend(resolved.Pixels.AsSpan(offset, 4), 255);
                        }
                        input = resolved.Pixels;
                        inputLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
                    }
                    if (SvgGaussianBlur.IsIdentity(deviation))
                    {
                        SvgPaintBounds inputBounds = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index]
                            : map.Regions?.SourceBounds ?? new(0, 0, width, height);
                        if (deviation.Horizontal > .03f || deviation.Vertical > .03f)
                        {
                            token.ThrowIfCancellationRequested();
                            float radiusX = deviation.Horizontal <= .03f ? 0 : MathF.Ceiling(3f * deviation.Horizontal);
                            float radiusY = deviation.Vertical <= .03f ? 0 : MathF.Ceiling(3f * deviation.Vertical);
                            inputBounds = new(MathF.Floor(inputBounds.Left - radiusX), MathF.Floor(inputBounds.Top - radiusY),
                                MathF.Ceiling(inputBounds.Right + radiusX), MathF.Ceiling(inputBounds.Bottom + radiusY));
                        }
                        imageBounds[index] = ClipImageBounds(inputBounds, index);
                    }
                    if (deviation.Horizontal > 135 || deviation.Vertical > 135)
                    {
                        SvgPaintBounds inputBounds = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index]
                            : map.Regions?.CapturedSourceBounds ?? map.Regions?.SourceBounds ?? new(0, 0, width, height);
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
                        SvgGaussianBlur.ApplyRescaled(input, output, width, height, deviation, inputBounds, regions[index], placement,
                            inputLinear, colorSpace, index == finalIndex && colorSpace == SvgFilterColorSpace.LinearRgb ? AllocateSamples() : Span<SvgShaderPaint>.Empty,
                            budget, token);
                        if (map.Regions is not null && deferredImage is null) { Clip(output, regions[index]); ClipOffsetSamples(regions[index]); }
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
                                SvgGaussianBlur.Apply(converted.Pixels, output, width, height, deviation, budget, token);
                                linear[index] = true;
                                if (map.Regions is not null && deferredImage is null) { Clip(output, regions[index]); ClipOffsetSamples(regions[index]); }
                                continue;
                            }
                            linear[index] = true;
                        }
                    }
                    SvgGaussianBlur.Apply(input, output, width, height, deviation, budget, token);
                    linear[index] |= inputLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Flood)
                {
                    uniform[index] = new(true, FloodPaint(primitive.Flood), false);
                    SvgConstantShaderPaint flood = FloodPaint(primitive.Flood).AsConstant(1f);
                    token.ThrowIfCancellationRequested();
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
                    token.ThrowIfCancellationRequested();
                    SvgPaintBounds sourceBounds = map.Regions?.SourceBounds ?? new(0, 0, width, height);
                    SvgPaintBounds firstBounds = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index] : sourceBounds;
                    SvgPaintBounds secondBounds = primitive.SecondInput.Index >= 0 ? imageBounds[primitive.SecondInput.Index] : sourceBounds;
                    SvgPaintBounds extent = CompositionBounds(firstBounds, secondBounds, primitive.Composition);
                    imageBounds[index] = ClipImageBounds(extent, index);
                    SvgPaintBounds wanted = requests[index], actual = imageBounds[index];
                    SvgPaintBounds demand = new(MathF.Max(wanted.Left, actual.Left), MathF.Max(wanted.Top, actual.Top),
                        MathF.Min(wanted.Right, actual.Right), MathF.Min(wanted.Bottom, actual.Bottom));
                    ComposeInputs(input, second, output, primitive.Input, primitive.SecondInput, inputLinear, secondLinear,
                        primitive.Composition, colorSpace, graphic, demand);
                    linear[index] = destinationLinear;

                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Offset)
                {
                    SvgPaintBounds? inputBounds = primitive.Input.Index >= 0 ? imageBounds[primitive.Input.Index] : map.Regions?.SourceBounds;
                    SvgPaintBounds bounds = inputBounds ?? new(0, 0, width, height);
                    SvgCpuBitmapOrigin nativeOrigin = map.Regions?.NativeMapping?.Origin ?? new((int)bounds.Left - 1, (int)bounds.Top - 1);
                    SvgShaderMatrix placed = new(1, 0, (float)(primitive.OffsetX * map.ScaleX), 0, 1, (float)(primitive.OffsetY * map.ScaleY));
                    SvgShaderMatrix? nativeTransform = null;
                    if (map.Regions?.NativeMapping is {} nativeMapping && nativeMapping.Coordinates.TryInvert(out SvgShaderMatrix parameterInverse))
                    {
                        token.ThrowIfCancellationRequested();
                        // Retain the conjugated matrix, including finite residual skew.
                        // Reducing it to a vector can select a different native image blitter.
                        placed = SvgShaderMatrix.Concat(nativeMapping.Coordinates,
                            SvgShaderMatrix.Concat(new(1, 0, (float)primitive.OffsetX, 0, 1, (float)primitive.OffsetY), parameterInverse));
                        SvgShaderMatrix forward = SvgShaderMatrix.Concat(new(1, 0, nativeOrigin.X, 0, 1, nativeOrigin.Y),
                            SvgShaderMatrix.Concat(placed, new(1, 0, -nativeOrigin.X, 0, 1, -nativeOrigin.Y)));
                        if (forward.ScaleX != 1 || forward.ScaleY != 1 || forward.SkewX != 0 || forward.SkewY != 0) nativeTransform = forward;
                    }
                    if (primitive.Input.Index < 0 || !uniform[primitive.Input.Index].Known)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgShaderMatrix imageTransform = nativeTransform ?? placed;
                        (float x0, float y0) = imageTransform.Map(bounds.Left, bounds.Top);
                        (float x1, float y1) = imageTransform.Map(bounds.Right, bounds.Top);
                        (float x2, float y2) = imageTransform.Map(bounds.Right, bounds.Bottom);
                        (float x3, float y3) = imageTransform.Map(bounds.Left, bounds.Bottom);
                        imageBounds[index] = ClipImageBounds(RoundLayerBounds(new(
                            MathF.Min(MathF.Min(x0, x1), MathF.Min(x2, x3)), MathF.Min(MathF.Min(y0, y1), MathF.Min(y2, y3)),
                            MathF.Max(MathF.Max(x0, x1), MathF.Max(x2, x3)), MathF.Max(MathF.Max(y0, y1), MathF.Max(y2, y3)))), index);
                    }
                    bool directSource = ResolvePaintSource(primitive.Input).Index < 0;
                    if (primitive.OffsetX == 0 && primitive.OffsetY == 0)
                    {
                        placed = new(1, 0, 0, 0, 1, 0);
                        nativeTransform = null;
                        imageBounds[index] = ClipImageBounds(bounds, index);
                    }
                    const float roundEpsilon = 1e-3f;
                    bool aligned = MathF.Abs(placed.ScaleX - 1) <= roundEpsilon && MathF.Abs(placed.ScaleY - 1) <= roundEpsilon
                        && MathF.Abs(placed.SkewX) <= roundEpsilon && MathF.Abs(placed.SkewY) <= roundEpsilon
                        && MathF.Abs(placed.TranslateX - MathF.Floor(placed.TranslateX + .5f)) <= roundEpsilon
                        && MathF.Abs(placed.TranslateY - MathF.Floor(placed.TranslateY + .5f)) <= roundEpsilon;
                    nearestSampling = !aligned;
                    if (index == finalPaintIndex && (directSource || primitive.Input.Index >= 0 && !uniform[primitive.Input.Index].Known)
                        && map.Regions is { DeferFinalOffset: true } && !aligned
                        && (primitive.OffsetX * map.ScaleX != Math.Truncate(primitive.OffsetX * map.ScaleX)
                            || primitive.OffsetY * map.ScaleY != Math.Truncate(primitive.OffsetY * map.ScaleY)))
                    {
                        // Fractional nearest transforms retain their source until the
                        // affine draw. Integer-aligned outputs use the ordinary stored
                        // result so color transfer precedes the later linear sampling.
                        SvgShaderMatrix imageToSource = SvgShaderMatrix.Concat(placed, new(1, 0, bounds.Left - nativeOrigin.X, 0, 1, bounds.Top - nativeOrigin.Y));
                        SvgFilterDeferredColorFilter pending = colorSpace == SvgFilterColorSpace.LinearRgb
                            ? SvgFilterDeferredColorFilter.LinearRgbRoundTrip
                            : ResolvePaintSource(primitive.Input) == SvgFilterInput.SourceAlpha ? SvgFilterDeferredColorFilter.SourceAlpha : SvgFilterDeferredColorFilter.None;
                        deferredImage = new(imageToSource, bounds, regions[index], SvgFilterImageSampling.Nearest) { ColorFilter = pending };
                        linear[index] = inputLinear;
                        token.ThrowIfCancellationRequested();
                        input.CopyTo(output);
                    }
                    else if (primitive.Input.Index >= 0 && uniform[primitive.Input.Index].Known)
                    {
                        float dx = placed.TranslateX, dy = placed.TranslateY;
                        if (nativeTransform is null && dx == MathF.Truncate(dx) && dy == MathF.Truncate(dy))
                        {
                            Offset(input, output, dx, dy, inputBounds, null, null, OffsetImagePaint.Packed, new(inputLinear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb, inputLinear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb, inputLinear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb), new(0, 0, width, height));
                            uniform[index] = uniform[primitive.Input.Index] with { HighPrecision = uniform[primitive.Input.Index].HighPrecision || colorSpace == SvgFilterColorSpace.LinearRgb };
                            SvgPaintBounds shifted = new(bounds.Left + dx, bounds.Top + dy, bounds.Right + dx, bounds.Bottom + dy);
                            SvgPaintBounds clip = imageBounds[index];
                            imageBounds[index] = new(Math.Max(shifted.Left, clip.Left), Math.Max(shifted.Top, clip.Top), Math.Min(shifted.Right, clip.Right), Math.Min(shifted.Bottom, clip.Bottom));
                        }
                        else
                        {
                            using SvgConverter.RasterizedSurface converted = Allocate();
                            SvgShaderPaint paint = uniform[primitive.Input.Index].Paint;
                            if (colorSpace == SvgFilterColorSpace.LinearRgb) paint = SvgFilterColorTransfer.Convert(paint, colorSpace);
                            SvgConstantShaderPaint constant = paint.AsConstant(1f);
                            token.ThrowIfCancellationRequested();
                            for (int y = 0; y < height; y++)
                            {
                                token.ThrowIfCancellationRequested();
                                for (int x = 0; x < width; x++)
                                    if (x >= MathF.Floor(bounds.Left) && x < MathF.Ceiling(bounds.Right) && y >= MathF.Floor(bounds.Top) && y < MathF.Ceiling(bounds.Bottom))
                                        constant.Blend(converted.Pixels.AsSpan((y * width + x) * 4, 4), 255);
                            }
                            // The allocated uniform image owns integer texels in
                            // either color space; logical crops remain separate.
                            SvgPaintBounds bitmapBounds = RoundLayerBounds(bounds);
                            Offset(converted.Pixels, output, dx, dy, bitmapBounds, null, nativeTransform, OffsetImagePaint.Packed, new(colorSpace, colorSpace, colorSpace), new(0, 0, width, height));
                            int tail = finalIndex;
                            while (tail > index)
                            {
                                token.ThrowIfCancellationRequested();
                                SvgFilterPrimitive alias = resource.Primitives.Items[tail];
                                if (alias.Kind == SvgFilterPrimitiveKind.GaussianBlur && (float)Math.Abs(alias.SigmaX * map.ScaleX) <= .03f && (float)Math.Abs(alias.SigmaY * map.ScaleY) <= .03f) tail = alias.Input.Index;
                                else if (alias.Kind == SvgFilterPrimitiveKind.Merge && alias.MergeCount == 1) tail = resource.MergeInputs.Items[alias.MergeFirst].Index;
                                else break;
                            }
                            if (tail == index && map.Regions?.DeferFinalOffset == true)
                            {
                                SvgPaintBounds storedBounds = RoundLayerBounds(bounds);
                                SvgShaderMatrix imageToSource = SvgShaderMatrix.Concat(placed,
                                    new(1, 0, storedBounds.Left - nativeOrigin.X, 0, 1, storedBounds.Top - nativeOrigin.Y));
                                // A nontrivial transform cannot defer a visible input
                                // layer crop. Resolve pending paint before that transform.
                                token.ThrowIfCancellationRequested();
                                SvgShaderPaint retained = paint;
                                bool resolvesUniformCrop = !ContainsDemand(nativeTransform ?? placed, storedBounds, requests[index]);
                                if (resolvesUniformCrop)
                                    retained = new(constant.Red / 255f, constant.Green / 255f, constant.Blue / 255f, constant.Alpha / 255f);
                                deferredImage = new(imageToSource, storedBounds, regions[finalIndex], SvgFilterImageSampling.Nearest) {
                                    Constant = retained,
                                    ColorFilter = resolvesUniformCrop ? SvgFilterDeferredColorFilter.None : SvgFilterDeferredColorFilter.UniformFill
                                };

                                static bool ContainsDemand(SvgShaderMatrix transform, SvgPaintBounds bounds, SvgPaintBounds demand)
                                {
                                    if (!transform.TryInvert(out SvgShaderMatrix inverse)) return false;
                                    const float inset = 1e-3f;
                                    for (int corner = 0; corner < 4; corner++)
                                    {
                                        float x = corner is 1 or 2 ? demand.Right - inset : demand.Left + inset;
                                        float y = corner >= 2 ? demand.Bottom - inset : demand.Top + inset;
                                        (x, y) = inverse.Map(x, y);
                                        if (x < bounds.Left || x > bounds.Right || y < bounds.Top || y > bounds.Bottom) return false;
                                    }
                                    return true;
                                }
                                token.ThrowIfCancellationRequested();
                                converted.Pixels.CopyTo(output);
                            }
                            else if (tail == index && nativeTransform is null && colorSpace == SvgFilterColorSpace.Srgb
                                && (!IsNearestSpriteTranslation(dx) || !IsNearestSpriteTranslation(dy)))
                            {
                                // Nearest sampling keeps uniform texels packed while
                                // final image-edge coverage blends against the caller.
                                // An enclosing opacity layer still consumes output bytes.
                                token.ThrowIfCancellationRequested();
                                SvgPaintBounds expanded = OffsetSamplingBounds(bitmapBounds, dx, dy);
                                SvgPaintBounds texels = new(bitmapBounds.Left + dx, bitmapBounds.Top + dy,
                                    bitmapBounds.Right + dx, bitmapBounds.Bottom + dy);
                                sampledUniform = new(new(constant.Red / 255f, constant.Green / 255f, constant.Blue / 255f, constant.Alpha / 255f),
                                    ClipImageBounds(texels, finalIndex), false)
                                {
                                    Coverage = SvgRectangleCoverage.Create(expanded.Left + dx, expanded.Top + dy,
                                        expanded.Right + dx, expanded.Bottom + dy, width, height)
                                };
                            }
                            else if (tail == index && nativeTransform is null && colorSpace == SvgFilterColorSpace.LinearRgb)
                            {
                                Span<SvgShaderPaint> values = AllocateSamples();
                                SvgPaintBounds expanded = OffsetSamplingBounds(bitmapBounds, dx, dy);
                                SvgRectangleCoverage edge = SvgRectangleCoverage.Create(expanded.Left + dx, expanded.Top + dy, expanded.Right + dx, expanded.Bottom + dy, width, height);
                                int left = (int)MathF.Floor(Math.Clamp(expanded.Left, 0, width)), top = (int)MathF.Floor(Math.Clamp(expanded.Top, 0, height));
                                int right = (int)MathF.Ceiling(Math.Clamp(expanded.Right, 0, width)), bottom = (int)MathF.Ceiling(Math.Clamp(expanded.Bottom, 0, height));
                                int moveX = (int)MathF.Floor(dx + .5f), moveY = (int)MathF.Floor(dy + .5f);
                                token.ThrowIfCancellationRequested();
                                for (int y = 0; y < height; y++)
                                {
                                    token.ThrowIfCancellationRequested();
                                    for (int x = 0; x < width; x++)
                                    {
                                        if (right <= left || bottom <= top) continue;
                                        byte coverage = edge.GetAlpha(x, y);
                                        int sx = Math.Clamp(x - moveX, left, right - 1), sy = Math.Clamp(y - moveY, top, bottom - 1);
                                        int at = (sy * width + sx) * 4;
                                        float alpha = coverage * (1f / 255f);
                                        values[y * width + x] = new(converted.Pixels[at] * (1f / 255f) * alpha, converted.Pixels[at + 1] * (1f / 255f) * alpha,
                                            converted.Pixels[at + 2] * (1f / 255f) * alpha, converted.Pixels[at + 3] * (1f / 255f) * alpha);
                                    }
                                }
                                linearOffsetSamples = true;
                            }
                        }
                    }
                    else
                    {
                        OffsetImagePaint imagePaint = colorSpace == SvgFilterColorSpace.LinearRgb && directSource && !inputLinear
                            ? OffsetImagePaint.Floating : OffsetImagePaint.Packed;
                        // Materializing a source-alpha shader keeps its float store;
                        // packed bitmap coverage has a different rounding boundary.
                        if (ResolvePaintSource(primitive.Input) == SvgFilterInput.SourceAlpha) imagePaint = OffsetImagePaint.SourceAlpha;
                        if (index == finalPaintIndex)
                        {
                            if (colorSpace == SvgFilterColorSpace.LinearRgb)
                            {
                                if (inputLinear) imagePaint = OffsetImagePaint.FloatingSamples;
                                else if (directSource) imagePaint = OffsetImagePaint.Floating;
                            }
                            else if (directSource && map.Regions?.PictureOutput == true) imagePaint = OffsetImagePaint.Constant;
                        }
                        Offset(input, output, primitive.OffsetX * map.ScaleX, primitive.OffsetY * map.ScaleY,
                            inputBounds, null, nativeTransform, imagePaint, new(inputLinear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb, inputLinear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb, inputLinear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb), new(0, 0, width, height));
                        if (map.Regions is not null)
                            RetainSampledImage(index, primitive.Input, nativeTransform ?? placed, bounds, aligned);

                    }
                    linear[index] = primitive.Input.Index >= 0 && uniform[primitive.Input.Index].Known && !uniform[index].Known
                        ? colorSpace == SvgFilterColorSpace.LinearRgb : inputLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Merge)
                {
                    if (primitive.MergeCount == 1)
                    {
                        SvgFilterInput merged = resource.MergeInputs.Items[primitive.MergeFirst];
                        token.ThrowIfCancellationRequested();
                        ReadInput(merged, index, graphic).CopyTo(output);
                        linear[index] = merged.Index >= 0 && linear[merged.Index];
                        RetainSampling(index, merged, false);
                        SvgPaintBounds sourceBounds = merged.Index >= 0 ? imageBounds[merged.Index]
                            : map.Regions?.SourceBounds ?? new(0, 0, width, height);
                        imageBounds[index] = ClipImageBounds(sourceBounds, index);
                        if (merged.Index >= 0 && uniform[merged.Index].Known)
                        {
                            uniform[index] = uniform[merged.Index] with { HighPrecision = uniform[merged.Index].HighPrecision || colorSpace == SvgFilterColorSpace.LinearRgb };
                        }
                        if (map.Regions is not null && deferredImage is null) { Clip(output, regions[index]); ClipOffsetSamples(regions[index]); }
                        continue;
                    }
                    bool destinationLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
                    SvgPaintBounds mergedImageBounds = default;
                    foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                    {
                        ReadOnlySpan<byte> layer = ReadInput(merged, index, graphic);
                        bool layerLinear = merged.Index >= 0 && linear[merged.Index];
                        Composite(layer, output, merged, layerLinear, colorSpace, graphic);
                        token.ThrowIfCancellationRequested();
                        SvgPaintBounds inputImageBounds = merged.Index >= 0 ? imageBounds[merged.Index]
                            : map.Regions?.SourceBounds ?? new(0, 0, width, height);
                        if (inputImageBounds.Right > inputImageBounds.Left && inputImageBounds.Bottom > inputImageBounds.Top)
                            mergedImageBounds = mergedImageBounds.Right <= mergedImageBounds.Left || mergedImageBounds.Bottom <= mergedImageBounds.Top
                                ? inputImageBounds : new(MathF.Min(mergedImageBounds.Left, inputImageBounds.Left), MathF.Min(mergedImageBounds.Top, inputImageBounds.Top),
                                    MathF.Max(mergedImageBounds.Right, inputImageBounds.Right), MathF.Max(mergedImageBounds.Bottom, inputImageBounds.Bottom));
                    }
                    imageBounds[index] = ClipImageBounds(RoundLayerBounds(mergedImageBounds), index);
                    linear[index] = destinationLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow)
                {
                    using SvgConverter.RasterizedSurface alpha = Allocate();
                    token.ThrowIfCancellationRequested();
                    for (int offset = 0; offset < bytes; offset += 4) { token.ThrowIfCancellationRequested(); alpha.Pixels[offset + 3] = input[offset + 3]; }
                    using SvgConverter.RasterizedSurface blurred = Allocate();
                    SvgGaussianBlur.Apply(alpha.Pixels, blurred.Pixels, width, height, deviation, budget, token);
                    double shadowX = primitive.OffsetX * map.ScaleX, shadowY = primitive.OffsetY * map.ScaleY;
                    bool fractionalShadow = shadowX != Math.Truncate(shadowX) || shadowY != Math.Truncate(shadowY);
                    bool retainedColorFilter = colorSpace == SvgFilterColorSpace.LinearRgb && !inputLinear
                        && deviation.Horizontal <= .03f && deviation.Vertical <= .03f;
                    SvgConstantShaderPaint tint = default;
                    if (fractionalShadow && !retainedColorFilter)
                    {
                        token.ThrowIfCancellationRequested();
                        tint = PrepareShadowShaderTint(primitive.Flood, colorSpace).AsConstant(1);
                    }
                    if (!fractionalShadow)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgShaderPaint nativeTint = PrepareShadowShaderTint(primitive.Flood, colorSpace);
                        SvgConstantShaderPaint packedTint = nativeTint.AsConstant(1);
                        // An effective-zero blur retains its input color filter. A nonzero
                        // blur materializes that image even when its pixel kernel only copies.
                        // The pending color conversion selects floating raster stages; the
                        // materialized image instead applies packed source-in tint lanes.

                        for (int offset = 0; offset < bytes; offset += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            byte coverage = blurred.Pixels[offset + 3];
                            if (!retainedColorFilter)
                            {
                                blurred.Pixels[offset] = Modulate(packedTint.Red, coverage);
                                blurred.Pixels[offset + 1] = Modulate(packedTint.Green, coverage);
                                blurred.Pixels[offset + 2] = Modulate(packedTint.Blue, coverage);
                                blurred.Pixels[offset + 3] = Modulate(packedTint.Alpha, coverage);
                                continue;
                            }
                            float weight = coverage * (1f / 255f);
                            Span<byte> pixel = blurred.Pixels.AsSpan(offset, 4);
                            pixel.Clear();
                            new SvgShaderPaint(nativeTint.Red * weight, nativeTint.Green * weight,
                                nativeTint.Blue * weight, nativeTint.Alpha * weight).Blend(pixel, 255);
                        }
                    }
                    if (fractionalShadow && retainedColorFilter)
                    {
                        // Pending color filters select floating sampling through
                        // the fractional transform, just as at integer offsets.
                        token.ThrowIfCancellationRequested();
                        SvgShaderPaint nativeTint = PrepareShadowShaderTint(primitive.Flood, colorSpace);
                        for (int y = 0; y < height; y++)
                        {
                            token.ThrowIfCancellationRequested();
                            for (int x = 0; x < width; x++)
                            {
                                float weight = SvgShaderPaint.SampleAffine(blurred.Pixels, width, height,
                                    x + .5f - (float)shadowX, y + .5f - (float)shadowY, new(0, 0), 1f).Alpha;
                                new SvgShaderPaint(nativeTint.Red * weight, nativeTint.Green * weight,
                                    nativeTint.Blue * weight, nativeTint.Alpha * weight).Blend(output.Slice((y * width + x) * 4, 4), 255);
                            }
                        }
                    }
                    else Offset(blurred.Pixels, output, shadowX, shadowY, null, fractionalShadow ? tint : null, null, OffsetImagePaint.Packed, new(colorSpace, colorSpace, colorSpace), new(0, 0, width, height));
                    bool destinationLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
                    bool convertSource = inputLinear != destinationLinear;
                    token.ThrowIfCancellationRequested();
                    for (int offset = 0; offset < bytes; offset += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        if (convertSource) ReadPaint(input, offset, inputLinear, colorSpace).Blend(output.Slice(offset, 4), 255);
                        else SvgConverter.BlendPremultipliedOver(output.Slice(offset, 4), input[offset], input[offset + 1], input[offset + 2], input[offset + 3]);
                    }
                    linear[index] = destinationLinear;
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter primitive is not implemented.");
                bool materialized = primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur
                    && (deviation.Horizontal > .03f || deviation.Vertical > .03f)
                    || primitive.Kind == SvgFilterPrimitiveKind.Merge && primitive.MergeCount != 1
                    || primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend
                        or SvgFilterPrimitiveKind.Flood or SvgFilterPrimitiveKind.DropShadow;
                if (materialized && map.Regions is not null && deferredImage is null)
                {
                    token.ThrowIfCancellationRequested();
                    SvgPaintBounds extent = imageBounds[index], wanted = requests[index];
                    imageBounds[index] = new(MathF.Max(extent.Left, wanted.Left), MathF.Max(extent.Top, wanted.Top),
                        MathF.Min(extent.Right, wanted.Right), MathF.Min(extent.Bottom, wanted.Bottom));
                    Clip(output, wanted);
                }
                if (primitive.Kind == SvgFilterPrimitiveKind.Offset
                    || primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur && deviation.Horizontal <= .03f && deviation.Vertical <= .03f)
                    RetainSampling(index, primitive.Input, nearestSampling);
                if (primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur && primitive.SigmaX == 0 && primitive.SigmaY == 0
                    && primitive.Input.Index >= 0 && uniform[primitive.Input.Index].Known)
                {
                    uniform[index] = uniform[primitive.Input.Index] with { HighPrecision = uniform[primitive.Input.Index].HighPrecision || colorSpace == SvgFilterColorSpace.LinearRgb };
                    SvgPaintBounds sourceBounds = imageBounds[primitive.Input.Index], clip = imageBounds[index];
                    imageBounds[index] = new(Math.Max(sourceBounds.Left, clip.Left), Math.Max(sourceBounds.Top, clip.Top), Math.Min(sourceBounds.Right, clip.Right), Math.Min(sourceBounds.Bottom, clip.Bottom));
                }
                if (map.Regions is not null && deferredImage is null) { Clip(output, regions[index]); ClipOffsetSamples(regions[index]); }
            }
            if (count > 0 && linear[finalIndex] && map.Regions?.DeferFinalColorTransfer != true)
            {
                AllocateSamples();
                if (linearOffsetSamples && samples is {} offsetValues)
                {
                    token.ThrowIfCancellationRequested();
                    for (int offset = 0; offset < bytes; offset += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgShaderPaint color = SvgFilterColorTransfer.Convert(offsetValues[offset / 4], SvgFilterColorSpace.Srgb);
                        offsetValues[offset / 4] = color;
                        outputs[finalIndex].Pixels.AsSpan(offset, 4).Clear();
                        color.Blend(outputs[finalIndex].Pixels.AsSpan(offset, 4), 255);
                    }
                }
                else ConvertColor(outputs[finalIndex].Pixels, outputs[finalIndex].Pixels, SvgFilterColorSpace.Srgb);
            }
            if (samples is not null && map.Regions is not null)
            {
                SvgPaintBounds finalRegion = regions[finalIndex];
                token.ThrowIfCancellationRequested();
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
            return new(outputs[finalIndex] with { }, samples, sampleOwner)
            {
                ColorSpace = count > 0 && map.Regions?.DeferFinalColorTransfer == true && linear[finalIndex]
                    ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb,
                DeferredImage = deferredImage,
                UniformPaint = count > 0 && uniform[finalIndex].Known
                    ? new(uniform[finalIndex].Paint, imageBounds[finalIndex], uniform[finalIndex].HighPrecision) : sampledUniform
            };
        }
        finally
        {
            if (!returned) sampleOwner?.Dispose();
            for (int index = 0; index < outputs.Length; index++)
                if ((!returned || index != finalIndex) && outputs[index].Pixels is not null) outputs[index].Dispose();
        }

        // Prepare immutable premultiplied sRGB lanes without allocation or ownership.
        // The enclosing operation observes pixel cancellation.
        static SvgShaderPaint FloodPaint(SvgSolidPaint paint)
            => new(paint.Color.Red / 255f * paint.Alpha, paint.Color.Green / 255f * paint.Alpha,
                paint.Color.Blue / 255f * paint.Alpha, paint.Alpha);

        // Effective-zero blurs and source aliases preserve the dynamic source's
        // paint path through crops. Other identity pixel kernels materialize an image.
        SvgFilterInput ResolvePaintSource(SvgFilterInput reference)
        {
            while (reference.Index >= 0)
            {
                token.ThrowIfCancellationRequested();
                SvgFilterPrimitive alias = resource.Primitives.Items[reference.Index];

                SvgFilterInput next;
                if (alias.Kind == SvgFilterPrimitiveKind.GaussianBlur && (float)Math.Abs(alias.SigmaX * map.ScaleX) <= .03f && (float)Math.Abs(alias.SigmaY * map.ScaleY) <= .03f
                    || alias.Kind == SvgFilterPrimitiveKind.Offset && alias.OffsetX == 0 && alias.OffsetY == 0) next = alias.Input;
                else if (alias.Kind == SvgFilterPrimitiveKind.Merge && alias.MergeCount == 1) next = resource.MergeInputs.Items[alias.MergeFirst];
                else break;
                if (next.Index >= reference.Index) throw new SvgException(SvgErrorCode.MalformedInput, "Filter aliases must refer to earlier results.");
                reference = next;
            }
            return reference;
        }

        // A retained alpha filter follows compatible sampling transforms. Two
        // nontrivial nearest transforms resolve the first image, consuming its
        // color filter. State lives on the existing owned output record.
        void RetainSampling(int index, SvgFilterInput reference, bool nearest)
        {
            token.ThrowIfCancellationRequested();
            SvgFilterDeferredColorFilter colorFilter = reference == SvgFilterInput.SourceAlpha
                ? SvgFilterDeferredColorFilter.SourceAlpha
                : reference.Index >= 0 ? outputs[reference.Index].SamplingColorFilter : SvgFilterDeferredColorFilter.None;
            SvgFilterImageSampling sampling = reference.Index >= 0
                ? outputs[reference.Index].FilterImageSampling : SvgFilterImageSampling.Linear;
            if (nearest)
            {
                if (sampling == SvgFilterImageSampling.Nearest) colorFilter = SvgFilterDeferredColorFilter.None;
                sampling = SvgFilterImageSampling.Nearest;
            }
            outputs[index] = outputs[index] with { SamplingColorFilter = colorFilter, FilterImageSampling = sampling };
            SvgFilterPrimitive primitive = resource.Primitives.Items[index];
            if (primitive.Kind is SvgFilterPrimitiveKind.GaussianBlur or SvgFilterPrimitiveKind.Merge
                && reference.Index >= 0 && sampledOffsets[reference.Index] is not null && map.Regions is not null)
                RetainSampledImage(index, reference, new(1, 0, 0, 0, 1, 0), imageBounds[reference.Index], true);
        }

        // Alias state and compatible transforms use the original forward map.
        // Invert once at each consumer, never invert an already rounded inverse
        // to reconstruct a transform. Pixel ownership remains with earlier nodes.
        void RetainSampledImage(int index, SvgFilterInput reference, SvgShaderMatrix transform,
            SvgPaintBounds bounds, bool compatible)
        {
            token.ThrowIfCancellationRequested();
            SvgPaintBounds region = RoundLayerBounds(regions[index]), wanted = requests[index];
            SvgPaintBounds clip = new(MathF.Max(region.Left, wanted.Left), MathF.Max(region.Top, wanted.Top),
                MathF.Min(region.Right, wanted.Right), MathF.Min(region.Bottom, wanted.Bottom));
            SvgFilterColorSpace operatingSpace = resource.Primitives.Items[index].ColorSpace ?? resource.ColorSpace;
            SampledOffset image = new(reference, transform, bounds, clip, operatingSpace);
            if (compatible && reference.Index >= 0 && sampledOffsets[reference.Index] is {} parent && parent.OperatingSpace == operatingSpace)
            {
                SvgPaintBounds inherited = MapClip(parent.Clip, transform);
                clip = new(MathF.Max(clip.Left, inherited.Left), MathF.Max(clip.Top, inherited.Top),
                    MathF.Min(clip.Right, inherited.Right), MathF.Min(clip.Bottom, inherited.Bottom));
                image = new(parent.Input, SvgShaderMatrix.Concat(transform, parent.InputToOutput), parent.Bounds, clip, operatingSpace);
            }
            if (image.InputToOutput.TryInvert(out _)) sampledOffsets[index] = image;

            static SvgPaintBounds MapClip(SvgPaintBounds bounds, SvgShaderMatrix transform)
            {
                float left = float.PositiveInfinity, top = float.PositiveInfinity;
                float right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                for (int corner = 0; corner < 4; corner++)
                {
                    (float x, float y) = transform.Map(corner is 1 or 2 ? bounds.Right : bounds.Left, corner >= 2 ? bounds.Bottom : bounds.Top);
                    left = MathF.Min(left, x); top = MathF.Min(top, y); right = MathF.Max(right, x); bottom = MathF.Max(bottom, y);
                }
                return RoundLayerBounds(new(left, top, right, bottom));
            }
        }

        // A shader can keep a layer crop when the crop contains the sampling
        // domain, or when it does not cut the finite image. Otherwise use the
        // already resolved draw so that image/crop coverage is materialized.
        bool TryPrepareSample(SvgFilterInput reference, SvgPaintBounds demand, out SvgShaderMatrix inverse)
        {
            inverse = default;
            if (reference.Index < 0 || sampledOffsets[reference.Index] is not {} image) return false;
            token.ThrowIfCancellationRequested();
            bool contained = demand.Left >= image.Clip.Left && demand.Top >= image.Clip.Top
                && demand.Right <= image.Clip.Right && demand.Bottom <= image.Clip.Bottom;
            if (!contained)
            {
                contained = true;
                for (int corner = 0; corner < 4; corner++)
                {
                    (float x, float y) = image.InputToOutput.Map(corner is 1 or 2 ? image.Bounds.Right : image.Bounds.Left,
                        corner >= 2 ? image.Bounds.Bottom : image.Bounds.Top);
                    contained &= x >= image.Clip.Left && x <= image.Clip.Right && y >= image.Clip.Top && y <= image.Clip.Bottom;
                }
            }
            return contained && image.InputToOutput.TryInvert(out inverse);
        }

        SvgPaintBounds ClipImageBounds(SvgPaintBounds bounds, int index)
        {
            if (map.Regions is null) return bounds;
            SvgPaintBounds clip = RoundLayerBounds(regions[index]);
            bounds = RoundLayerBounds(bounds);
            return new(MathF.Max(bounds.Left, clip.Left), MathF.Max(bounds.Top, clip.Top),
                MathF.Min(bounds.Right, clip.Right), MathF.Min(bounds.Bottom, clip.Bottom));
        }

        void ClipOffsetSamples(SvgPaintBounds bounds)
        {
            if (!linearOffsetSamples || samples is not {} values) return;
            token.ThrowIfCancellationRequested();
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                    if (x < MathF.Floor(bounds.Left) || x >= MathF.Ceiling(bounds.Right) || y < MathF.Floor(bounds.Top) || y >= MathF.Ceiling(bounds.Bottom)) values[y * width + x] = default;
            }
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
            try { return new(width, height, new byte[bytes], owner); }
            catch { owner.Dispose(); throw; }
        }

        ReadOnlySpan<byte> ReadInput(SvgFilterInput reference, int before, ReadOnlySpan<byte> graphic)
        {
            if (reference == SvgFilterInput.SourceGraphic) return graphic;
            if (reference == SvgFilterInput.SourceAlpha)
            {
                if (outputs[count].Pixels is null)
                {
                    token.ThrowIfCancellationRequested();
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

        void Composite(ReadOnlySpan<byte> input, Span<byte> output, SvgFilterInput reference, bool inputLinear, SvgFilterColorSpace colorSpace, ReadOnlySpan<byte> graphic)
        {
            SvgFilterInput sampledReference = reference;
            // Offsets and a zero blur retain the source's image/color-filter consumer.
            while (reference.Index >= 0)
            {
                token.ThrowIfCancellationRequested();
                SvgFilterPrimitive source = resource.Primitives.Items[reference.Index];
                if (source.Kind != SvgFilterPrimitiveKind.Offset
                    && (source.Kind != SvgFilterPrimitiveKind.GaussianBlur || source.SigmaX != 0 || source.SigmaY != 0)) break;
                if (source.Input.Index >= reference.Index)
                    throw new SvgException(SvgErrorCode.MalformedInput, "Filter graph input is outside its earlier results.");
                reference = source.Input;
            }
            if (sampledReference.Index >= 0 && !uniform[sampledReference.Index].Known
                && sampledOffsets[sampledReference.Index] is {} retained)
            {
                SvgShaderMatrix transform = retained.InputToOutput;
                SvgShaderMatrix? matrix = transform.ScaleX == 1 && transform.ScaleY == 1
                    && transform.SkewX == 0 && transform.SkewY == 0 ? null : transform;
                ReadOnlySpan<byte> image = ReadInput(retained.Input, sampledReference.Index, graphic);
                OffsetImagePaint retainedPaint = retained.Input == SvgFilterInput.SourceAlpha ? OffsetImagePaint.SourceAlpha : OffsetImagePaint.Packed;
                Offset(image, output, transform.TranslateX, transform.TranslateY, retained.Bounds, null, matrix, retainedPaint,
                    new(retained.Input.Index >= 0 && linear[retained.Input.Index] ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb, retained.OperatingSpace, colorSpace), retained.Clip);
                return;
            }
            token.ThrowIfCancellationRequested();
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                // A merge draws stored inputs into a bitmap. Without a transfer,
                // native direct image draws retain packed source-over rounding.
                // Color-filter inputs, including SourceAlpha, retain their shader route.
                if (ReadUniform(sampledReference, offset, colorSpace) is {} constant)
                    constant.AsConstant(1f).Blend(output.Slice(offset, 4), 255);
                else if (reference != SvgFilterInput.SourceAlpha && inputLinear == (colorSpace == SvgFilterColorSpace.LinearRgb))
                    SvgConverter.BlendPremultipliedOver(output.Slice(offset, 4), input[offset], input[offset + 1], input[offset + 2], input[offset + 3]);
                else ReadPaint(input, offset, inputLinear, colorSpace).Blend(output.Slice(offset, 4), 255);
            }
        }

        void ComposeInputs(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> output,
            SvgFilterInput firstReference, SvgFilterInput secondReference, bool firstLinear, bool secondLinear, SvgFilterComposition composition,
            SvgFilterColorSpace colorSpace, ReadOnlySpan<byte> graphic, SvgPaintBounds demand)
        {
            bool destinationLinear = colorSpace == SvgFilterColorSpace.LinearRgb;
            token.ThrowIfCancellationRequested();
            bool firstSampled = TryPrepareSample(firstReference, demand, out SvgShaderMatrix firstInverse);
            bool secondSampled = TryPrepareSample(secondReference, demand, out SvgShaderMatrix secondInverse);
            if (firstSampled || secondSampled) token.ThrowIfCancellationRequested();
            bool highPrecision = firstLinear != destinationLinear || secondLinear != destinationLinear
                || IsAlpha(firstReference) || IsAlpha(secondReference);
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                if (!highPrecision)
                {
                    // Sampling geometry does not itself require floating color.
                    // Preserve the existing packed composition when no transfer
                    // or alpha color filter selects the floating pipeline.
                    SvgConstantShaderPaint a = firstSampled ? Sample(firstReference, firstInverse, offset, colorSpace, graphic).AsConstant(1f)
                        : ReadUniform(firstReference, offset, colorSpace) is {} ca ? ca.AsConstant(1f)
                        : new(first[offset], first[offset + 1], first[offset + 2], first[offset + 3]);
                    SvgConstantShaderPaint b = secondSampled ? Sample(secondReference, secondInverse, offset, colorSpace, graphic).AsConstant(1f)
                        : ReadUniform(secondReference, offset, colorSpace) is {} cb ? cb.AsConstant(1f)
                        : new(second[offset], second[offset + 1], second[offset + 2], second[offset + 3]);
                    output[offset] = Packed(a.Red, b.Red, a.Alpha, b.Alpha, composition);
                    output[offset + 1] = Packed(a.Green, b.Green, a.Alpha, b.Alpha, composition);
                    output[offset + 2] = Packed(a.Blue, b.Blue, a.Alpha, b.Alpha, composition);
                    output[offset + 3] = Packed(a.Alpha, b.Alpha, a.Alpha, b.Alpha, composition);
                    continue;
                }
                SvgShaderPaint firstPaint = firstSampled ? Sample(firstReference, firstInverse, offset, colorSpace, graphic)
                    : ReadUniform(firstReference, offset, colorSpace) ?? ReadPaint(first, offset, firstLinear, colorSpace);
                SvgShaderPaint secondPaint = secondSampled ? Sample(secondReference, secondInverse, offset, colorSpace, graphic)
                    : ReadUniform(secondReference, offset, colorSpace) ?? ReadPaint(second, offset, secondLinear, colorSpace);
                float sourceAlpha = firstPaint.Alpha, destinationAlpha = secondPaint.Alpha;
                SvgShaderPaint sample = new(Channel(firstPaint.Red, secondPaint.Red),
                    Channel(firstPaint.Green, secondPaint.Green),
                    Channel(firstPaint.Blue, secondPaint.Blue),
                    Channel(sourceAlpha, destinationAlpha));
                sample.Blend(output.Slice(offset, 4), 255);

                float Channel(float s, float d)
                {
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

        SvgShaderPaint Sample(SvgFilterInput reference, SvgShaderMatrix inverse, int offset, SvgFilterColorSpace colorSpace, ReadOnlySpan<byte> graphic)
        {
            SampledOffset image = sampledOffsets[reference.Index] ?? throw new SvgException(SvgErrorCode.MalformedInput, "Missing sampled filter image.");
            int px = offset / 4 % width, py = offset / 4 / width;
            if (px < image.Clip.Left || px >= image.Clip.Right || py < image.Clip.Top || py >= image.Clip.Bottom) return default;
            (float x, float y) = inverse.Map(offset / 4 % width + .5f, offset / 4 / width + .5f);
            // Native nearest image shaders include the right/bottom edge and
            // snap integer sample positions left/up by one representable value.
            // This is sample selection, independently of image-draw coverage.
            if (x <= image.Bounds.Left || x > image.Bounds.Right || y <= image.Bounds.Top || y > image.Bounds.Bottom) return default;
            int column = (int)MathF.Floor(MathF.BitDecrement(x)), row = (int)MathF.Floor(MathF.BitDecrement(y));
            if ((uint)column >= (uint)width || (uint)row >= (uint)height) return default;
            ReadOnlySpan<byte> input = ReadInput(image.Input, reference.Index, graphic);
            bool sourceLinear = image.Input.Index >= 0 && linear[image.Input.Index];
            return ReadPaint(input, (row * width + column) * 4, sourceLinear, colorSpace);
        }

        bool IsAlpha(SvgFilterInput reference)
        {
            while (reference.Index >= 0)
            {
                token.ThrowIfCancellationRequested();
                SvgFilterPrimitive primitive = resource.Primitives.Items[reference.Index];
                if (primitive.Kind != SvgFilterPrimitiveKind.Offset
                    && (primitive.Kind != SvgFilterPrimitiveKind.GaussianBlur || primitive.SigmaX != 0 || primitive.SigmaY != 0)) return false;
                reference = primitive.Input;
            }
            return reference == SvgFilterInput.SourceAlpha;
        }

        static byte Packed(int source, int destination, int sourceAlpha, int destinationAlpha, SvgFilterComposition composition)
        {
            int value = composition switch
            {
                SvgFilterComposition.In => Divide(source * destinationAlpha),
                SvgFilterComposition.Screen => source + destination - Divide(source * destination),
                SvgFilterComposition.Multiply => (source * (255 - destinationAlpha) + destination * (255 - sourceAlpha) + source * destination + 255) >> 8,
                _ => source + Divide(destination * (255 - sourceAlpha))
            };
            return (byte)Math.Clamp(value, 0, 255);
            static int Divide(int value) { value += 128; return (value + (value >> 8)) >> 8; }
        }

        SvgShaderPaint? ReadUniform(SvgFilterInput reference, int offset, SvgFilterColorSpace destination)
        {
            if (reference.Index < 0 || !uniform[reference.Index].Known) return null;
            SvgShaderPaint color = uniform[reference.Index].Paint;
            SvgPaintBounds bounds = imageBounds[reference.Index];
            int x = offset / 4 % width, y = offset / 4 / width;
            if (x < MathF.Floor(bounds.Left) || x >= MathF.Ceiling(bounds.Right)
                || y < MathF.Floor(bounds.Top) || y >= MathF.Ceiling(bounds.Bottom)) return default(SvgShaderPaint);
            return destination == SvgFilterColorSpace.Srgb ? color : SvgFilterColorTransfer.Convert(color, destination);
        }

        static SvgShaderPaint ReadPaint(ReadOnlySpan<byte> input, int offset, bool linear, SvgFilterColorSpace destination)
        {
            const float byteScale = 1f / 255f;
            SvgShaderPaint sample = new(input[offset] * byteScale, input[offset + 1] * byteScale,
                input[offset + 2] * byteScale, input[offset + 3] * byteScale);
            return linear == (destination == SvgFilterColorSpace.LinearRgb) ? sample : SvgFilterColorTransfer.Convert(sample, destination);
        }

        bool HasColor(ReadOnlySpan<byte> input)
        {
            token.ThrowIfCancellationRequested();
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                if (input[offset] != 0 || input[offset + 1] != 0 || input[offset + 2] != 0) return true;
            }
            return false;
        }

        void ConvertColor(ReadOnlySpan<byte> input, Span<byte> output, SvgFilterColorSpace destination)
        {
            token.ThrowIfCancellationRequested();
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


        void Clip(Span<byte> pixels, SvgPaintBounds region)
        {
            int left = Bound(region.Left, width, false), right = Bound(region.Right, width, true);
            int top = Bound(region.Top, height, false), bottom = Bound(region.Bottom, height, true);
            token.ThrowIfCancellationRequested();
            for (int row = 0; row < height; row++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> scan = pixels.Slice(row * width * 4, width * 4);
                if (row < top || row >= bottom || right <= left) scan.Clear();
                else { scan[..(left * 4)].Clear(); scan[(right * 4)..].Clear(); }
            }
            static int Bound(float value, int maximum, bool upper)
                => value <= 0 ? 0 : value >= maximum ? maximum : upper ? (int)Math.Ceiling(value) : (int)Math.Floor(value);
        }

        static byte Modulate(byte value, byte alpha) => (byte)((value * alpha + 127) / 255);

        void Offset(ReadOnlySpan<byte> input, Span<byte> output, double dx, double dy, SvgPaintBounds? imageBounds, SvgConstantShaderPaint? shadow, SvgShaderMatrix? nativeTransform, OffsetImagePaint imagePaint, ImageColorTransfer transfer, SvgPaintBounds outputClip)
        {
            ValidateOffset(dx, dy);
            token.ThrowIfCancellationRequested();
            SvgPaintBounds clipping = RoundLayerBounds(outputClip);
            int clipLeft = (int)Math.Clamp(clipping.Left, 0, width), clipTop = (int)Math.Clamp(clipping.Top, 0, height);
            int clipRight = (int)Math.Clamp(clipping.Right, 0, width), clipBottom = (int)Math.Clamp(clipping.Bottom, 0, height);
            if (clipRight <= clipLeft || clipBottom <= clipTop) return;
            int conversionSteps = (transfer.Source == transfer.Working ? 0 : 1) + (transfer.Working == transfer.Destination ? 0 : 1);
            bool convertImage = conversionSteps != 0;
            if (convertImage) token.ThrowIfCancellationRequested();
            bool fractional = dx != Math.Truncate(dx) || dy != Math.Truncate(dy);
            if (fractional && shadow is {} flood)
            {
                // DropShadow uses a linearly sampled matrix translation, while
                // ordinary feOffset retains nearest-neighbor displacement.
                // The source-in color filter follows interpolation and retains
                // its independently quantized constant premultiplied lanes.
                token.ThrowIfCancellationRequested();
                for (int y = clipTop; y < clipBottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    long qy = (long)MathF.Floor(65536f * (y + .5f - (float)dy) + .5f) - 32768;
                    long top = qy >> 16;
                    int fractionY = unchecked((short)(qy ^ 0x8000));
                    for (int x = clipLeft; x < clipRight; x++)
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
            }
            SvgPaintBounds bounds = imageBounds ?? new(0, 0, width, height);
            // The SVG filter scope retains the union of source and displaced
            // drawable bounds. Its transparent padding must stay in the image;
            // nearest sampling clamps to this capture, not to painted geometry.
            bounds = OffsetSamplingBounds(bounds, (float)dx, (float)dy);
            if (fractional && nativeTransform is null)
            {
                token.ThrowIfCancellationRequested();
                // A nearest image translation whose AA rectangle rounds to the
                // same four-bit pixel grid takes the native sprite path. Its
                // image-edge AA is absent; the source's own coverage remains.
                fractional = !IsNearestSpriteTranslation((float)dx) || !IsNearestSpriteTranslation((float)dy);
            }

            if (nativeTransform is {} forward)
            {
                OffsetTransformed(input, output, bounds, forward);
                return;
            }
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(bounds.Left + (float)dx,
                bounds.Top + (float)dy, bounds.Right + (float)dx, bounds.Bottom + (float)dy, width, height);
            int imageLeft = (int)MathF.Floor(Math.Clamp(bounds.Left, 0, width));
            int imageTop = (int)MathF.Floor(Math.Clamp(bounds.Top, 0, height));
            int imageRight = (int)MathF.Ceiling(Math.Clamp(bounds.Right, 0, width));
            int imageBottom = (int)MathF.Ceiling(Math.Clamp(bounds.Bottom, 0, height));
            if (imageRight <= imageLeft || imageBottom <= imageTop) return;
            if (fractional) token.ThrowIfCancellationRequested();
            if (fractional && imagePaint is OffsetImagePaint.Floating or OffsetImagePaint.FloatingSamples or OffsetImagePaint.SourceAlpha)
                token.ThrowIfCancellationRequested();
            Span<SvgShaderPaint> coveredSamples = fractional && imagePaint == OffsetImagePaint.FloatingSamples
                ? AllocateSamples() : Span<SvgShaderPaint>.Empty;
            if (!coveredSamples.IsEmpty) linearOffsetSamples = true;
            // SkImageFilters::Offset uses nearest sampling, retaining the legacy
            // layer-space pixel displacement rather than bilinear interpolation.
            if (dx != Math.Truncate(dx)) dx = Math.Floor((float)dx + .5f);
            if (dy != Math.Truncate(dy)) dy = Math.Floor((float)dy + .5f);
            ValidateOffset(dx, dy);
            token.ThrowIfCancellationRequested();
            for (int y = clipTop; y < clipBottom; y++)
            {
                token.ThrowIfCancellationRequested();
                long sy = (long)y - (int)dy;
                for (int x = clipLeft; x < clipRight; x++)
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
                        if (convertImage)
                        {
                            ReadImagePaint(input, at).Blend(pixel, alpha);
                            continue;
                        }
                        // Linear image coverage stays unquantized through color transfer.
                        // Source paint follows its destination; other stored inputs stay packed.
                        if (!coveredSamples.IsEmpty)
                        {
                            float weight = alpha * (1f / 255f);
                            SvgShaderPaint sample = new(input[at] * (1f / 255f) * weight, input[at + 1] * (1f / 255f) * weight,
                                input[at + 2] * (1f / 255f) * weight, input[at + 3] * (1f / 255f) * weight);
                            coveredSamples[y * width + x] = sample;
                            sample.Blend(pixel, 255);
                            continue;
                        }
                        if (imagePaint == OffsetImagePaint.SourceAlpha)
                        {
                            new SvgShaderPaint(0, 0, 0, input[at + 3] / 255f).Blend(pixel, alpha);
                            continue;
                        }
                        if (imagePaint == OffsetImagePaint.Floating)
                        {
                            SvgShaderPaint paint = new(input[at] / 255f, input[at + 1] / 255f, input[at + 2] / 255f, input[at + 3] / 255f);
                            SvgFilterColorTransfer.Convert(SvgFilterColorTransfer.Convert(paint, SvgFilterColorSpace.LinearRgb), SvgFilterColorSpace.Srgb).Blend(pixel, alpha);
                            continue;
                        }
                        if (imagePaint == OffsetImagePaint.Constant)
                        {
                            new SvgConstantShaderPaint(input[at], input[at + 1], input[at + 2], input[at + 3]).Blend(pixel, alpha);
                            continue;
                        }
                        new SvgConstantShaderPaint(input[at], input[at + 1], input[at + 2], input[at + 3]).BlendBitmap(pixel, alpha);
                        continue;
                    }
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                    {
                        int at = (int)(sy * width + sx) * 4;
                        if (convertImage || imagePaint == OffsetImagePaint.SourceAlpha) ReadImagePaint(input, at)
                            .Blend(output.Slice((y * width + x) * 4, 4), 255);
                        else new SvgConstantShaderPaint(input[at], input[at + 1], input[at + 2], input[at + 3])
                            .BlendBitmap(output.Slice((y * width + x) * 4, 4), 255);
                    }
                }
            }

            SvgShaderPaint ReadImagePaint(ReadOnlySpan<byte> image, int at)
            {
                SvgShaderPaint sample = ReadPaint(image, at, transfer.Source == SvgFilterColorSpace.LinearRgb, transfer.Working);
                return transfer.Working == transfer.Destination ? sample : SvgFilterColorTransfer.Convert(sample, transfer.Destination);
            }

            // A conjugated image matrix uses the shared native fill-edge walker
            // and constant shader arithmetic. Axis translations above retain
            // their distinct rectangle/packed-bitmap route.
            void OffsetTransformed(ReadOnlySpan<byte> image, Span<byte> target, SvgPaintBounds imageBounds, SvgShaderMatrix transform)
            {
                if (!transform.TryInvert(out SvgShaderMatrix inverse)) return;
                int firstX = (int)MathF.Floor(Math.Clamp(imageBounds.Left, 0, width));
                int firstY = (int)MathF.Floor(Math.Clamp(imageBounds.Top, 0, height));
                int lastX = (int)MathF.Ceiling(Math.Clamp(imageBounds.Right, 0, width));
                int lastY = (int)MathF.Ceiling(Math.Clamp(imageBounds.Bottom, 0, height));
                if (lastX <= firstX || lastY <= firstY) return;
                Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
                points[0] = Point(imageBounds.Left, imageBounds.Top);
                points[1] = Point(imageBounds.Right, imageBounds.Top);
                points[2] = Point(imageBounds.Right, imageBounds.Bottom);
                points[3] = Point(imageBounds.Left, imageBounds.Bottom);
                SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                    [], [], 0, width, 0, height, width, height, new(0, width), new SvgAnalyticClipContext(new(clipLeft, clipTop, clipRight, clipBottom)),
                    budget, out bool eligible, token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Transformed filter image edges exceed the retained coverage boundary.");
                    token.ThrowIfCancellationRequested();
                    for (int y = clipTop; y < clipBottom; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        walker.PrepareRow(y, token);
                        token.ThrowIfCancellationRequested();
                        for (int x = clipLeft; x < clipRight; x++)
                        {
                            if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                            float px = x + .5f, py = y + .5f;
                            float u = px * inverse.ScaleX + (py * inverse.SkewX + inverse.TranslateX);
                            float v = px * inverse.SkewY + (py * inverse.ScaleY + inverse.TranslateY);
                            int sampleX = (int)Math.Clamp(MathF.Floor(u), firstX, lastX - 1);
                            int sampleY = (int)Math.Clamp(MathF.Floor(v), firstY, lastY - 1);
                            int at = (sampleY * width + sampleX) * 4;
                            SvgAnalyticPaint sampledPaint = convertImage || imagePaint == OffsetImagePaint.SourceAlpha
                                ? SvgAnalyticPaint.Shader(ReadImagePaint(image, at))
                                : SvgAnalyticPaint.Constant(new(image[at], image[at + 1], image[at + 2], image[at + 3]));
                            walker.PaintPixel(x, target.Slice((y * width + x) * 4, 4), sampledPaint, SvgAnalyticClipCoverage.Full);
                        }
                    }
                }
                finally { walker.Dispose(); }

                SvgDevicePoint Point(float x, float y)
                {
                    (float px, float py) = transform.Map(x, y);
                    return new(px, py);
                }
            }
        }

        // Retain displaced capture padding separately from the finite colored
        // texels. Pure shared setup; callers own cancellation.
        static SvgPaintBounds OffsetSamplingBounds(SvgPaintBounds bounds, float dx, float dy)
            => new(MathF.Min(bounds.Left, MathF.Floor(bounds.Left + dx)), MathF.Min(bounds.Top, MathF.Floor(bounds.Top + dy)),
                MathF.Max(bounds.Right, MathF.Ceiling(bounds.Right + dx)), MathF.Max(bounds.Bottom, MathF.Ceiling(bounds.Bottom + dy)));

        static void ValidateOffset(double dx, double dy)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx < int.MinValue || dx > int.MaxValue || dy < int.MinValue || dy > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Filter offset exceeds the numeric range.");
        }
    }
}
