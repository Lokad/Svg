using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgConversionApiContractTests
{
    public enum OutputForm { Owned, Memory, Stream, AsyncOwned, AsyncMemory, AsyncStream }

    public static IEnumerable<object[]> Cases()
    {
        // Each input reaches a distinct renderer/preparation path. Numerical
        // boundaries and native pixel references belong to their own tests.
        foreach (string id in new[] { "solid", "curve", "image", "filter", "resources", "text", "positioned-path", "shared-path" })
        foreach (bool prepared in new[] { false, true })
        foreach (OutputForm form in Enum.GetValues<OutputForm>()) yield return [id, prepared, form];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Output_forms_preserve_bytes_input_and_destination_ownership(string id, bool prepared, OutputForm form)
    {
        byte[] input = id switch
        {
            "text" => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/text-node-boundaries/source-items-span-ffi-16-start-gradient-candidate.svg")),
            "positioned-path" => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/text-node-boundaries/path-position-activation-41-candidate.svg")),
            "shared-path" => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/text-node-boundaries/shared-layout.svg")),
            _ => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'>" + (id switch
            {
                "solid" => "<rect x='2' y='2' width='16' height='16' fill='red' fill-opacity='.37'/>",
                "curve" => "<path d='M2 20C2 1 21 2 20 20Z' fill='blue' stroke='red' stroke-width='.5'/>",
                "image" => "<image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' width='18' height='18'/>",
                "filter" => "<defs><filter id='f' x='0' y='0' width='24' height='24' filterUnits='userSpaceOnUse'><feFlood flood-color='blue' flood-opacity='.37' result='t'/><feComposite in='SourceGraphic' in2='t' operator='in'/><feOffset dx='.25'/></filter></defs><circle cx='12' cy='12' r='8' fill='red' filter='url(#f)'/>",
                "resources" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='2' height='4' fill='blue'/></pattern><g id='item'><rect width='20' height='20' fill='url(#p)'/></g></defs><g opacity='.5'><use href='#item' x='.25' y='.5'/></g>",
                _ => throw new ArgumentOutOfRangeException(nameof(id))
            }) + "</svg>")
        };
        byte[] unchanged = input.ToArray();
        bool needsFont = id is "text" or "positioned-path" or "shared-path";
        double scale = id == "positioned-path" ? 1.5 : 1;
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData/text-node-boundaries/font-pack.json")), "public-noto-sans");
        byte[] fontBytes = needsFont
            ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/text-paint-bounds/sans.ttf")) : [];
        FontProvider provider = new(fontBytes);
        using SvgFontSnapshot? fonts = needsFont ? await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None) : null;
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        byte[] destination = Enumerable.Repeat((byte)0xA5, expected.Length + 17).ToArray();
        using MemoryStream stream = new();
        byte[] actual;
        if (prepared)
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
                new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
            switch (form)
            {
                case OutputForm.Owned: actual = document.ConvertToPng(CancellationToken.None).ToArray(); break;
                case OutputForm.AsyncOwned: actual = (await document.ConvertToPngAsync(CancellationToken.None)).ToArray(); break;
                case OutputForm.Memory: int count = document.ConvertToPng(destination, CancellationToken.None); Assert.Equal(expected.Length, count); actual = destination[..count]; break;
                case OutputForm.AsyncMemory: int asyncCount = await document.ConvertToPngAsync(destination, CancellationToken.None); Assert.Equal(expected.Length, asyncCount); actual = destination[..asyncCount]; break;
                case OutputForm.Stream: Assert.Equal(expected.Length, document.ConvertToPng(stream, CancellationToken.None)); actual = stream.ToArray(); break;
                case OutputForm.AsyncStream: Assert.Equal(expected.Length, await document.ConvertToPngAsync(stream, CancellationToken.None)); actual = stream.ToArray(); break;
                default: throw new ArgumentOutOfRangeException(nameof(form));
            }
        }
        else
        {
            switch (form)
            {
                case OutputForm.Owned: actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(); break;
                case OutputForm.AsyncOwned: actual = (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray(); break;
                case OutputForm.Memory: int count = SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None); Assert.Equal(expected.Length, count); actual = destination[..count]; break;
                case OutputForm.AsyncMemory: int asyncCount = await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None); Assert.Equal(expected.Length, asyncCount); actual = destination[..asyncCount]; break;
                case OutputForm.Stream: Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None)); actual = stream.ToArray(); break;
                case OutputForm.AsyncStream: Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, stream, options, CancellationToken.None)); actual = stream.ToArray(); break;
                default: throw new ArgumentOutOfRangeException(nameof(form));
            }
        }
        Assert.Equal(expected, actual);
        Assert.Equal(unchanged, input);
        Assert.All(destination.AsSpan(expected.Length).ToArray(), value => Assert.Equal((byte)0xA5, value));
        Assert.True(stream.CanWrite);
    }

    private sealed class FontProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        /// <summary>Returns borrowed immutable bytes through snapshot disposal, observes cancellation and allows concurrent requests; returned sources own no storage.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length == 0) throw new InvalidOperationException("The font-free API fixture must not acquire a font.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, bytes));
        }
    }
}
