using Lokad.Svg;
using Lokad.Svg.Fonts;

ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
if (png.Length < pngSignature.Length || !png.Span[..pngSignature.Length].SequenceEqual(pngSignature)) throw new InvalidOperationException("The installed Lokad.Svg package did not produce a PNG.");

byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'/>"u8.ToArray();
byte[] storage = new byte[1_024];
svg.CopyTo(storage, 0);
byte[] original = (byte[])storage.Clone();
SvgRenderLimits limits = new(200_000, 1_024, 1, 16, 1_024, 16, 16, 1_000);
SvgRenderOptions limitedOptions = new(null, 1, SvgColor.Transparent, limits);
foreach (bool asynchronous in new[] { false, true })
{
    try
    {
        if (asynchronous) await SvgConverter.ConvertToPngAsync(storage.AsMemory(0, svg.Length), storage, SvgRenderOptions.Default, CancellationToken.None);
        else SvgConverter.ConvertToPng(storage.AsMemory(0, svg.Length), storage, SvgRenderOptions.Default, CancellationToken.None);
        throw new InvalidOperationException("The installed package accepted overlapping input/output memory.");
    }
    catch (ArgumentException exception) when (exception.ParamName == "destination")
    {
        if (!storage.AsSpan().SequenceEqual(original)) throw new InvalidOperationException("Overlap rejection modified caller storage.");
    }

    try
    {
        if (asynchronous) await SvgConverter.ConvertToPngAsync(svg, limitedOptions, CancellationToken.None);
        else SvgConverter.ConvertToPng(svg, limitedOptions, CancellationToken.None);
        throw new InvalidOperationException("The installed package accepted an oversized output dimension.");
    }
    catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.LimitExceeded) { }
}

Console.WriteLine("Installed package conversion, overlap rejection and dimension-limit scenarios passed.");

SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse("""
    {
      "packId": "test-pack",
      "files": [{ "relativePath": "files/example.ttf", "byteSize": 1, "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }],
      "families": [{ "requestedFamily": "Example", "resolvedFamily": "Example", "relativeFontFile": "files/example.ttf" }]
    }
    """u8.ToArray(), "test-pack");
SvgFontPackResolution resolution = catalog.Resolve(new SvgFontFaceRequest("example", 400, false));
if (resolution.IsFallback || resolution.Face.File.RelativePath != "files/example.ttf") throw new InvalidOperationException("The installed package did not resolve the compatible font-pack manifest.");

Console.WriteLine("Installed package font-pack catalog scenario passed.");
