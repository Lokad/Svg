using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Maps primitive distances into the already placed device source grid.</summary>
internal readonly record struct SvgFilterDeviceMap(double ScaleX, double ScaleY)
{
    internal SvgFilterRegionContext? Regions { get; init; }
}

/// <summary>Retains one integer-offset source branch and its composed intermediate crop.</summary>
internal readonly record struct SvgOffsetFilterPlacement(double X, double Y, bool AlphaOnly, SvgPaintBounds Clip);

/// <summary>Maps primitive coordinates to the bounded source grid, separately from Gaussian distances.</summary>
internal readonly record struct SvgFilterRegionContext(SvgAffineTransform Coordinates, SvgViewportLengthBases LengthBases, SvgPaintBounds FilterRegion)
{
    internal SvgPaintBounds? SourceBounds { get; init; }
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
            + ParserCollectionMemory.GetArrayByteCount<SvgPaintBounds>(count) + ParserCollectionMemory.GetArrayByteCount<bool>(count), SvgMemoryCategory.Surface);
        SvgConverter.RasterizedSurface[] outputs = new SvgConverter.RasterizedSurface[count + 2];
        SvgPaintBounds[] regions = new SvgPaintBounds[count];
        bool[] linear = new bool[count];
        long work = 0;
        bool returned = false;
        SvgShaderPaint[]? samples = null;
        RenderBudget.BudgetReservation? sampleOwner = null;
        int finalIndex = count == 0 ? 0 : count - 1;
        try
        {
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
                bool inputLinear = primitive.Input.Index >= 0 && primitive.Input.Index < index && linear[primitive.Input.Index];
                ReadOnlySpan<byte> input = ReadInput(primitive.Input, index, graphic);
                outputs[index] = Allocate();
                Span<byte> output = outputs[index].Pixels;
                if (map.Regions is {} coordinates)
                {
                    SvgPaintBounds defaults = primitive.Input.Index >= 0 ? regions[primitive.Input.Index] : coordinates.FilterRegion;
                    if (primitive.Kind == SvgFilterPrimitiveKind.Merge && primitive.MergeCount > 0)
                    {
                        bool first = true;
                        foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                        {
                            Charge(1);
                            if (merged.Index < 0) { defaults = coordinates.FilterRegion; break; }
                            if ((uint)merged.Index >= (uint)index) throw new SvgException(SvgErrorCode.MalformedInput, "Filter merge input is outside its earlier results.");
                            SvgPaintBounds region = regions[merged.Index];
                            if (first) { defaults = region; first = false; }
                            else defaults = new(Math.Min(defaults.Left, region.Left), Math.Min(defaults.Top, region.Top),
                                Math.Max(defaults.Right, region.Right), Math.Max(defaults.Bottom, region.Bottom));
                        }
                    }
                    regions[index] = ResolveRegion(primitive.Region, defaults, coordinates);
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
                        work += SvgGaussianBlur.ApplyRescaled(input, output, width, height, deviation, inputBounds,
                            inputLinear, resource.ColorSpace, index == finalIndex ? AllocateSamples() : Span<SvgShaderPaint>.Empty,
                            budget, availableWork - work, token);
                        if (map.Regions is not null) Clip(output, regions[index], CropRounding.Outward);
                        continue;
                    }
                    if (resource.ColorSpace == SvgFilterColorSpace.LinearRgb && (deviation.Horizontal > .03f || deviation.Vertical > .03f))
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
                else if (primitive.Kind == SvgFilterPrimitiveKind.Offset)
                {
                    Offset(input, output, primitive.OffsetX * map.ScaleX, primitive.OffsetY * map.ScaleY);
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
                    bool destinationLinear = resource.ColorSpace == SvgFilterColorSpace.LinearRgb;
                    foreach (SvgFilterInput merged in resource.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                    {
                        ReadOnlySpan<byte> layer = ReadInput(merged, index, graphic);
                        bool layerLinear = merged.Index >= 0 && linear[merged.Index];
                        if (layerLinear == destinationLinear) Composite(layer, output);
                        else
                        {
                            using SvgConverter.RasterizedSurface converted = Allocate();
                            ConvertColor(layer, converted.Pixels, resource.ColorSpace);
                            Composite(converted.Pixels, output);
                        }
                    }
                    linear[index] = destinationLinear;
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.DropShadow)
                {
                    if (resource.ColorSpace == SvgFilterColorSpace.LinearRgb && (primitive.Flood.Red != 0 || primitive.Flood.Green != 0 || primitive.Flood.Blue != 0))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Colored linearRGB shadow conversion is not implemented.");
                    if (resource.ColorSpace == SvgFilterColorSpace.LinearRgb)
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
                    Offset(blurred.Pixels, output, primitive.OffsetX * map.ScaleX, primitive.OffsetY * map.ScaleY);
                    Charge(pixels);
                    for (int offset = 0; offset < bytes; offset += 4)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgConverter.BlendPremultipliedOver(output.Slice(offset, 4), input[offset], input[offset + 1], input[offset + 2], input[offset + 3]);
                    }
                    linear[index] = inputLinear;
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

        void Composite(ReadOnlySpan<byte> input, Span<byte> output)
        {
            Charge(pixels);
            for (int offset = 0; offset < bytes; offset += 4)
            {
                token.ThrowIfCancellationRequested();
                const float byteScale = 1f / 255f;
                new SvgShaderPaint(input[offset] * byteScale, input[offset + 1] * byteScale,
                    input[offset + 2] * byteScale, input[offset + 3] * byteScale).Blend(output.Slice(offset, 4), 255);
            }
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

        SvgPaintBounds ResolveRegion(SvgFilterPrimitiveRegion? authored, SvgPaintBounds defaults, SvgFilterRegionContext context)
        {
            if (authored is not {} region) return defaults;
            Charge(4);
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

        void Offset(ReadOnlySpan<byte> input, Span<byte> output, double dx, double dy)
        {
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
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                        input.Slice((int)(sy * width + sx) * 4, 4).CopyTo(output.Slice((y * width + x) * 4, 4));
                }
            }
        }

        static void ValidateOffset(double dx, double dy)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx < int.MinValue || dx > int.MaxValue || dy < int.MinValue || dy > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Filter offset exceeds the numeric range.");
            if (dx != Math.Truncate(dx) || dy != Math.Truncate(dy))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fractional filter offsets are not implemented.");
        }
    }
}
