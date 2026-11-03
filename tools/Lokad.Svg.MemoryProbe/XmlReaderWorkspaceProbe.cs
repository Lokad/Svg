using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using ReadOnlyMemoryStream = Lokad.Svg.Parsing.ReadOnlyMemoryStream;

namespace Lokad.Svg.MemoryProbe;

/// <summary>Measures one warmed BCL traversal with the shared reader policy and its peak workspace admission.</summary>
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
            traversal.WorkspaceBytes,
            traversal.TouchedCharacters,
            traversal.TextNodeCount,
            traversal.ReaderSetupManagedAllocatedBytes,
            traversal.ReaderTraversalManagedAllocatedBytes,
            traversal.AttributeAccessManagedAllocatedBytes);

        static void WarmReader(SvgRenderLimits limits)
        {
            Traverse("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), limits);
            Traverse("<?xml version='1.0'?><!DOCTYPE svg><svg/>"u8.ToArray(), limits);
        }
    }

    private static XmlReaderTraversalMeasurement Traverse(ReadOnlyMemory<byte> svg, SvgRenderLimits limits)
    {
        long setupStart = GC.GetTotalAllocatedBytes(precise: true);
        RenderBudget budget = new();
        using RenderBudget.BudgetReservation workspace = budget.Reserve(
            SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
        XmlReaderSettings settings = SvgXmlReaderPolicy.Create(svg, limits, budget, CancellationToken.None);
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
        return new XmlReaderTraversalMeasurement(touchedCharacters, textNodeCount, readerSetupManagedAllocatedBytes,
            readerTraversalManagedAllocatedBytes, attributeAccessManagedAllocatedBytes, budget.Snapshot().PeakBytes);
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
    long AttributeAccessManagedAllocatedBytes,
    long WorkspaceBytes);
