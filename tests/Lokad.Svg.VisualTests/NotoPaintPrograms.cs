using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

/// <summary>Shares the immutable Noto programs and catalog used by native paint fixtures.</summary>
internal sealed class NotoPaintPrograms : ISvgFontProgramSourceProvider
{
    private static readonly Lazy<SvgFontPackCatalog> PaintCatalog = new(static () =>
        SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "text-paint-bounds/font-pack.json")), "r99-fonts"));
    private static readonly Lazy<byte[]> Regular = new(static () => File.ReadAllBytes(
        Path.Combine(ResearchCorpus.Root, "supplied-font-labels/fonts/NotoSans-Regular.ttf")));
    private static readonly Lazy<byte[]> Mono = new(static () => File.ReadAllBytes(
        Path.Combine(ResearchCorpus.Root, "supplied-font-documents/fonts/NotoSansMono-Bold.ttf")));

    internal static SvgFontPackCatalog Catalog => PaintCatalog.Value;

    /// <summary>Returns immutable process-lifetime borrowed bytes, observes cancellation and permits concurrent requests.</summary>
    /// <remarks>The fixture owns the host inputs. Each snapshot retains its ordinary acquisition,
    /// admission and disposal; sharing these inputs retains no renderer reservations.</remarks>
    public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = file.RelativePath switch
        {
            "sans.ttf" => Regular.Value,
            "mono.ttf" => Mono.Value,
            _ => throw new InvalidDataException("Unknown fixture font program.")
        };
        return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, bytes));
    }
}
