namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal static partial class OpenTypeFont
{
    /// <summary>Identifies newly admitted composite fitting dependencies while preserving earlier instruction-free composite replay.</summary>
    internal static bool NeedsCompositeDeviceFitting(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        using OpenTypeGlyphRecursionTracker active = new(MaximumCompositeGlyphDepth + 1, budget, work, token);
        return Visit(glyphIndex, 0);

        bool Visit(ushort glyph, int depth)
        {
            work.Consume(token);
            if (depth > MaximumCompositeGlyphDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "The composite glyph nesting depth exceeds the supported limit.");
            if (!active.TryEnter(glyph)) throw InvalidFont("The composite glyph graph contains a cycle.");
            try
            {
                OpenTypeGlyphHeader? header = GetGlyphHeader(bytes, glyph, metrics, work, token);
                if (header is null || !header.Value.IsComposite) return false;
                using OpenTypeCompositeGlyphComponentStorage components = GetBudgetedCompositeGlyphComponents(bytes, glyph, metrics, budget, work, token);
                foreach (OpenTypeCompositeGlyphComponent component in components.Components)
                {
                    work.Consume(token);
                    if ((component.Flags & (4 | 256 | 512)) != 0 || Visit(component.GlyphIndex, depth + 1)) return true;
                }
                return false;
            }
            finally { active.Exit(glyph); }
        }
    }

    /// <summary>Assembles owned, ordered component points either in font units or by fitting each child before its parent.</summary>
    /// <remarks>Depth, component/point counts and cycles are bounded. Temporary and returned arrays reserve geometry bytes before allocation; every visit/copy observes work and cancellation. The optional fitter borrows one supplied face and interpreter, and never owns returned points.</remarks>
    private static OpenTypeGlyphPointOutline GetGlyphPointOutline(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, CompositeTrueTypeHinting? fitting, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        using OpenTypeGlyphRecursionTracker active = new(MaximumCompositeGlyphDepth + 1, budget, work, token);
        return Expand(glyphIndex, 0);

        OpenTypeGlyphPointOutline Expand(ushort glyph, int depth)
        {
            work.Consume(token);
            if (depth > MaximumCompositeGlyphDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "The composite glyph nesting depth exceeds the supported limit.");
            if (!active.TryEnter(glyph)) throw InvalidFont("The composite glyph graph contains a cycle.");
            try
            {
                OpenTypeSimpleGlyphStructureStorage structure = GetGlyphStructure(bytes, glyph, metrics, budget, work, token, true);
                if (structure.CompositeHeader is null)
                {
                    OpenTypeGlyphPointStorage? decoded = null;
                    try
                    {
                        decoded = GetSimpleGlyphPoints(bytes, structure.Value, budget, work, token);
                        OpenTypeTableRange instructions = default;
                        if (structure.Value.GlyphRange.Length > 10)
                        {
                            int offset = structure.Value.GlyphRange.Offset + 10 + structure.Value.ContourEndpoints.Length * 2;
                            instructions = new(offset + 2, ReadUInt16(bytes.Span, offset));
                        }
                        OpenTypeHorizontalPhantoms? phantoms = fitting?.Fit(glyph, decoded.Points, structure.Value.ContourEndpoints, structure.Value.GlyphRange, instructions, false, null);
                        return new(decoded.Points, structure.Value.ContourEndpoints, decoded, structure, phantoms);
                    }
                    catch { decoded?.Dispose(); structure.Dispose(); throw; }
                }
                OpenTypeTableRange range = structure.Value.GlyphRange;
                structure.Dispose();
                using OpenTypeCompositeGlyphComponentStorage components = GetBudgetedCompositeGlyphComponents(bytes, glyph, metrics, budget, work, token);
                using BudgetedParserList<OpenTypeGlyphPoint> points = new(budget, SvgMemoryCategory.Geometry, work, token);
                using BudgetedParserList<ushort> ends = new(budget, SvgMemoryCategory.Geometry, work, token);
                OpenTypeHorizontalPhantoms? selectedMetrics = null;
                foreach (OpenTypeCompositeGlyphComponent component in components.Components)
                {
                    work.Consume(token);
                    using OpenTypeGlyphPointOutline child = Expand(component.GlyphIndex, depth + 1);
                    // Metrics are inherited before the component's outline transform/offset.
                    // A later marked component replaces an earlier selection, including nested children.
                    if ((component.Flags & 512) != 0) selectedMetrics = child.Phantoms;
                    if (child.Points.Length > MaximumSimpleGlyphPoints - points.Count) throw new SvgException(SvgErrorCode.LimitExceeded, "The expanded composite point count exceeds the supported limit.");
                    if (child.Ends.Length > MaximumSimpleGlyphContours - ends.Count) throw new SvgException(SvgErrorCode.LimitExceeded, "The expanded composite contour count exceeds the supported limit.");
                    int basePoint = points.Count;
                    int dx = fitting is null ? component.HorizontalOffset : fitting.ScaleOffset(component.HorizontalOffset, (component.Flags & 4) != 0, true);
                    int dy = fitting is null ? component.VerticalOffset : fitting.ScaleOffset(component.VerticalOffset, (component.Flags & 4) != 0, false);
                    foreach (OpenTypeGlyphPoint point in child.Points)
                    {
                        work.Consume(token);
                        int x = Transform(point.X, component.Transform.XX, point.Y, component.Transform.XY, dx);
                        int y = Transform(point.X, component.Transform.YX, point.Y, component.Transform.YY, dy);
                        points.Add(new(x, y, point.IsOnCurve));
                    }
                    foreach (ushort end in child.Ends) { work.Consume(token); ends.Add(checked((ushort)(basePoint + end))); }
                }
                OwnedParserArray<OpenTypeGlyphPoint> ownedPoints = points.Detach();
                try
                {
                    OwnedParserArray<ushort> ownedEnds = ends.Detach();
                    try
                    {
                        OpenTypeHorizontalPhantoms? phantoms = fitting?.Fit(glyph, ownedPoints.Items, ownedEnds.Items, range, components.Instructions, true, selectedMetrics);
                        return new(ownedPoints.Items, ownedEnds.Items, ownedPoints, ownedEnds, phantoms);
                    }
                    catch { ownedEnds.Dispose(); throw; }
                }
                catch { ownedPoints.Dispose(); throw; }
            }
            finally { active.Exit(glyph); }
        }

        static int Transform(int x, double xx, int y, double xy, int offset)
        {
            // Each F2Dot14 multiplication rounds before the sum, as in the native fixed-point loader.
            double first = Math.CopySign(Math.Floor(Math.Abs(x * xx) + .5), x * xx);
            double second = Math.CopySign(Math.Floor(Math.Abs(y * xy) + .5), y * xy);
            double result = first + second + offset;
            if (result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "A composite point exceeds its signed coordinate bound.");
            return (int)result;
        }
    }
}

/// <summary>Retains the fitted horizontal metric points in device coordinates; component placement does not transform them. Vertical phantom access remains unsupported.</summary>
internal readonly record struct OpenTypeHorizontalPhantoms(int LeftX, int LeftY, int RightX, int RightY);

/// <summary>Owns ordered glyph points and contour ends until fitting or quadratic replay finishes. Owners are exclusive; disposal releases both arrays on every exit.</summary>
internal sealed class OpenTypeGlyphPointOutline : IDisposable
{
    private readonly IDisposable _pointsOwner, _endsOwner;
    internal OpenTypeGlyphPointOutline(OpenTypeGlyphPoint[] points, ushort[] ends, IDisposable pointsOwner, IDisposable endsOwner, OpenTypeHorizontalPhantoms? phantoms)
    {
        Points = points; Ends = ends; _pointsOwner = pointsOwner; _endsOwner = endsOwner; Phantoms = phantoms;
    }
    internal OpenTypeGlyphPoint[] Points { get; }
    internal ushort[] Ends { get; }
    internal OpenTypeHorizontalPhantoms? Phantoms { get; }
    public void Dispose() { _pointsOwner.Dispose(); _endsOwner.Dispose(); }
}
