using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Maps primitive distances into the already placed device source grid.</summary>
internal readonly record struct SvgFilterDeviceMap(double ScaleX, double ScaleY);

/// <summary>Evaluates an ordered graph on one bounded premultiplied grid.</summary>
/// <remarks>Inputs and resources are borrowed and immutable. Each output has an admitted surface owner;
/// all intermediates are released on success, failure or cancellation. The final owner transfers to
/// the caller. Work includes every copy, conversion, blur pass and composition. Calls are independent.</remarks>
internal static class SvgFilterPlayback
{
    internal static SvgConverter.RasterizedSurface Apply(ReadOnlySpan<byte> source, int width, int height,
        SvgFilterResource resource, SvgFilterDeviceMap map, RenderBudget budget, long availableWork, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > int.MaxValue / 4 || source.Length != pixels * 4)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Filter frame exceeds contiguous RGBA storage.");
        int bytes = source.Length;
        int count = resource.Primitives.Items.Length;
        using RenderBudget.BudgetReservation tableOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgConverter.RasterizedSurface>(count + 1), SvgMemoryCategory.Surface);
        SvgConverter.RasterizedSurface[] outputs = new SvgConverter.RasterizedSurface[count + 1];
        long work = 0;
        bool returned = false;
        int finalIndex = count == 0 ? 0 : count - 1;
        try
        {
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
                ReadOnlySpan<byte> input;
                if (primitive.Input == SvgFilterInput.SourceGraphic) input = source;
                else if (primitive.Input == SvgFilterInput.SourceAlpha)
                {
                    if (outputs[count].Pixels is null)
                    {
                        Charge(pixels);
                        outputs[count] = Allocate();
                        for (int offset = 0; offset < bytes; offset += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            outputs[count].Pixels[offset + 3] = source[offset + 3];
                        }
                    }
                    input = outputs[count].Pixels;
                }
                else if ((uint)primitive.Input.Index < (uint)index) input = outputs[primitive.Input.Index].Pixels;
                else throw new SvgException(SvgErrorCode.MalformedInput, "Filter graph input is outside its earlier results.");
                outputs[index] = Allocate();
                Span<byte> output = outputs[index].Pixels;
                SvgGaussianDeviation deviation = new((float)(primitive.SigmaX * Math.Abs(map.ScaleX)), (float)(primitive.SigmaY * Math.Abs(map.ScaleY)));
                if (primitive.Kind == SvgFilterPrimitiveKind.GaussianBlur)
                {
                    if (resource.ColorSpace == SvgFilterColorSpace.LinearRgb && (deviation.Horizontal > .03f || deviation.Vertical > .03f))
                    {
                        Charge(pixels);
                        for (int offset = 0; offset < bytes; offset += 4)
                        {
                            token.ThrowIfCancellationRequested();
                            if (input[offset] != 0 || input[offset + 1] != 0 || input[offset + 2] != 0)
                                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Colored linearRGB blur conversion is not implemented.");
                        }
                    }
                    work += SvgGaussianBlur.Apply(input, output, width, height, deviation, budget, availableWork - work, token);
                }
                else if (primitive.Kind == SvgFilterPrimitiveKind.Offset)
                    Offset(input, output, primitive.OffsetX * map.ScaleX, primitive.OffsetY * map.ScaleY);
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
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter primitive is not implemented.");
            }
            token.ThrowIfCancellationRequested();
            returned = true;
            return outputs[finalIndex] with { WorkUnits = work };
        }
        finally
        {
            for (int index = 0; index < outputs.Length; index++)
                if ((!returned || index != finalIndex) && outputs[index].Pixels is not null) outputs[index].Dispose();
        }

        SvgConverter.RasterizedSurface Allocate()
        {
            token.ThrowIfCancellationRequested();
            RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(bytes), SvgMemoryCategory.Surface);
            try { return new(width, height, new byte[bytes], owner, 0); }
            catch { owner.Dispose(); throw; }
        }

        void Charge(long amount)
        {
            if (amount > availableWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter graph exceeds the configured work limit.");
            work += amount;
        }

        void Offset(ReadOnlySpan<byte> input, Span<byte> output, double dx, double dy)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx < int.MinValue || dx > int.MaxValue || dy < int.MinValue || dy > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Filter offset exceeds the numeric range.");
            if (dx != Math.Truncate(dx) || dy != Math.Truncate(dy))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fractional filter offsets are not implemented.");
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
    }
}
