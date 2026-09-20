using Lokad.Svg;

ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
if (png.Length < pngSignature.Length || !png.Span[..pngSignature.Length].SequenceEqual(pngSignature)) throw new InvalidOperationException("The installed Lokad.Svg package did not produce a PNG.");
Console.WriteLine("Installed package conversion passed.");
