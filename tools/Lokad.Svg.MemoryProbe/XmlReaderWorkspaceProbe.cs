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
        XmlReaderTraversalMeasurement traversal = Traverse(svg, limits);
        long managedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationStart;
        return new XmlReaderWorkspaceMeasurement(
            managedAllocatedBytes,
            SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, limits.MaximumAttributesPerElement),
            traversal.TouchedCharacters,
            traversal.TextNodeCount,
            traversal.ReaderSetupManagedAllocatedBytes,
            traversal.ReaderTraversalManagedAllocatedBytes,
            traversal.AttributeAccessManagedAllocatedBytes);
    }

    private static void WarmReader(SvgRenderLimits limits)
    {
        ReadOnlyMemory<byte> warmInput = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        Traverse(warmInput, limits);
        Traverse(warmInput, limits);
    }

    private static XmlReaderTraversalMeasurement Traverse(ReadOnlyMemory<byte> svg, SvgRenderLimits limits)
    {
        long setupStart = GC.GetTotalAllocatedBytes(precise: true);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaximumInputBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        long touchedCharacters = 0;
        long textNodeCount = 0;
        long attributeAccessManagedAllocatedBytes = 0;
        using ReadOnlyMemoryStream stream = new(svg);
        using XmlReader reader = XmlReader.Create(stream, settings);
        long readerSetupManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - setupStart;
        long traversalStart = GC.GetTotalAllocatedBytes(precise: true);
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) textNodeCount = checked(textNodeCount + 1);
            if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes) continue;
            while (reader.MoveToNextAttribute())
            {
                long attributeAccessStart = GC.GetTotalAllocatedBytes(precise: true);
                string name = reader.Name;
                string value = reader.Value;
                attributeAccessManagedAllocatedBytes = checked(attributeAccessManagedAllocatedBytes + GC.GetTotalAllocatedBytes(precise: true) - attributeAccessStart);
                touchedCharacters = checked(touchedCharacters + name.Length + value.Length);
            }
            reader.MoveToElement();
        }
        long readerTraversalManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - traversalStart;
        return new XmlReaderTraversalMeasurement(touchedCharacters, textNodeCount, readerSetupManagedAllocatedBytes, readerTraversalManagedAllocatedBytes, attributeAccessManagedAllocatedBytes);
    }
}

internal readonly record struct XmlReaderWorkspaceMeasurement(
    long ManagedAllocatedBytes,
    long WorkspaceBytes,
    long TouchedCharacters,
    long TextNodeCount,
    long ReaderSetupManagedAllocatedBytes,
    long ReaderTraversalManagedAllocatedBytes,
    long AttributeAccessManagedAllocatedBytes);

internal readonly record struct XmlReaderTraversalMeasurement(
    long TouchedCharacters,
    long TextNodeCount,
    long ReaderSetupManagedAllocatedBytes,
    long ReaderTraversalManagedAllocatedBytes,
    long AttributeAccessManagedAllocatedBytes);
