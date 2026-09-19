using System.Xml;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.MemoryProbe;

/// <summary>Measures one warmed BCL XML-reader traversal using the renderer's stream and reader settings.</summary>
internal static class XmlReaderWorkspaceProbe
{
    internal static XmlReaderWorkspaceMeasurement Measure(ReadOnlyMemory<byte> svg, SvgRenderLimits limits)
    {
        WarmReader(limits);
        long allocationStart = GC.GetTotalAllocatedBytes(precise: true);
        long touchedCharacters = Traverse(svg, limits);
        long managedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationStart;
        return new XmlReaderWorkspaceMeasurement(managedAllocatedBytes, SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, limits.MaximumAttributesPerElement), touchedCharacters);
    }

    private static void WarmReader(SvgRenderLimits limits)
    {
        ReadOnlyMemory<byte> warmInput = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        Traverse(warmInput, limits);
        Traverse(warmInput, limits);
    }

    private static long Traverse(ReadOnlyMemory<byte> svg, SvgRenderLimits limits)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaximumInputBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        long touchedCharacters = 0;
        using ReadOnlyMemoryStream stream = new(svg);
        using XmlReader reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes) continue;
            while (reader.MoveToNextAttribute()) touchedCharacters = checked(touchedCharacters + reader.Name.Length + reader.Value.Length);
            reader.MoveToElement();
        }
        return touchedCharacters;
    }
}

internal readonly record struct XmlReaderWorkspaceMeasurement(long ManagedAllocatedBytes, long WorkspaceBytes, long TouchedCharacters);
