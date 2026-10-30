using Lokad.Svg;
using Lokad.Svg.Fonts;
using Lokad.Svg.Testing;

ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
if (png.Length < pngSignature.Length || !png.Span[..pngSignature.Length].SequenceEqual(pngSignature)) throw new InvalidOperationException("The installed Lokad.Svg package did not produce a PNG.");

using (TestMemoryBudget sharedBudget = new(1_000_000))
{
    SvgRenderOptions governedOptions = SvgRenderOptions.Default.WithMemoryBudget(sharedBudget);
    using (SvgPngResult retained = SvgConverter.ConvertToOwnedPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), governedOptions, CancellationToken.None))
    {
        if (sharedBudget.Reserved != retained.Memory.Length) throw new InvalidOperationException("The installed result lost its retained charge.");
        sharedBudget.Limit = sharedBudget.Reserved;
        try { using SvgPngResult refused = SvgConverter.ConvertToOwnedPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"u8.ToArray(), governedOptions, CancellationToken.None); throw new InvalidOperationException("The shared root did not refuse overlapping storage."); }
        catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.LimitExceeded) { }
    }
    if (sharedBudget.Reserved != 0 || sharedBudget.Disposed) throw new InvalidOperationException("Result cleanup did not preserve the borrowed budget.");
}
Console.WriteLine("Installed package shared admission and retained-result ownership passed.");

byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'/>"u8.ToArray();
byte[] storage = new byte[1_024];
svg.CopyTo(storage, 0);
byte[] original = (byte[])storage.Clone();
SvgRenderLimits limits = new(1_024, 1, 16, 1_024, 16, 16);
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

byte[] scalable = "<svg xmlns='http://www.w3.org/2000/svg' width='4800' height='2400'><rect width='100%' height='100%' fill='red'/></svg>"u8.ToArray();
SvgDocumentDimensions dimensions = SvgConverter.InspectDimensions(scalable, SvgRenderOptions.Default, CancellationToken.None);
if (dimensions.LogicalViewport.Width != 4800 || dimensions.LogicalViewport.Height != 2400)
    throw new InvalidOperationException("The installed package did not inspect logical dimensions.");
SvgRasterBounds bounds = new(800, 600, 180_000);
SvgRasterSize planned = dimensions.GetRasterSize(1, bounds, SvgRenderLimits.Default);
if (planned.Width != 600 || planned.Height != 300) throw new InvalidOperationException("The installed package did not plan bounded output.");
ReadOnlyMemory<byte> fitted = SvgConverter.ConvertToPng(scalable, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null, bounds), CancellationToken.None);
if (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(fitted.Span[16..]) != planned.Width
    || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(fitted.Span[20..]) != planned.Height)
    throw new InvalidOperationException("The installed package did not preserve its raster sizing plan.");
SvgConverter.InspectDimensions("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Absent'>A</text></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
try
{
    SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect id='r' width='1' height='1' stroke-linejoin='arcs'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
    throw new InvalidOperationException("The installed package accepted an unsupported line join.");
}
catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnsupportedFeature)
{
    if (exception.SourceDiagnostic is not { ElementName: "rect", ElementId: "r", AttributeName: "stroke-linejoin", AttributeValue: "arcs", LineNumber: 1 })
        throw new InvalidOperationException("The installed package omitted actionable source context.");
}
Console.WriteLine("Installed package font-free inspection, bounded output and source diagnostic scenarios passed.");

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

await HttpFontQualification.RunAsync();
