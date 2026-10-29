// Adapts Chromium's SizeF and bounded drawing-rectangle rules.
// Copyright 2012 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Applies native drawing bounds to point lists and non-scaling strokes after the complete source and its markers are lowered.</summary>
/// <remarks>Replaces culled geometry with inert anchors, retaining object bounds for
/// enclosing resources and eligible filters. Failure releases the batch. Temporary command storage
/// shares the parser memory budget and cancellation token. Calls are parser-thread confined; no caller storage is retained.</remarks>
internal static class SvgNativeVisualBounds
{
    internal static SvgParsedGeometry Cull(SvgParsedGeometry parsed, XmlReader reader, SvgPaintableElementContext context,
        SvgRecordedPaintStyle paint, SvgViewportFrame? frame, int maximumCommands, SvgDocumentState state)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (parsed.Geometries.IsEmpty || !style.NonScalingStroke && reader.LocalName is not ("polygon" or "polyline")) return parsed;
        // Ordinary cached sources still have a pending instance/viewBox map.
        // Their live drawing bounds cannot be decided in definition coordinates.
        if (!style.NonScalingStroke && (reader is SvgDefinitionLeafSource || state.DefinitionsDepth >= 0 || state.SymbolDepth >= 0)) return parsed;
        bool picture = state.IsResourcePicture || state.PatternDepth >= 0 || state.MaskDepth >= 0;
        if (picture && (context.CanPaintEmptyNativeBounds || reader.GetAttribute("transform") is {} ownTransform && !SvgCssWhitespace.Trim(ownTransform).AsSpan().IsEmpty))
            return parsed;
        CancellationToken token = context.CancellationToken;
        try
        {
            SvgAffineTransform placement = SvgViewportFrame.MapGeometry(context.ResolveTransform(reader), frame);
            if (!placement.TryInvert(out SvgAffineTransform inverse)) return parsed;
            SvgAffineTransform host = context.StrokeHostMap.ComposeChecked(placement);
            SvgClipBox visual = default;
            float controlLeft = float.PositiveInfinity, controlTop = float.PositiveInfinity, controlRight = float.NegativeInfinity, controlBottom = float.NegativeInfinity;
            bool line = reader.LocalName == "line";
            if (paint.SourceObjectBox is {} source)
            {
                Union(Box(source.Left, source.Top, source.Right - source.Left, source.Bottom - source.Top));
                Control(new(source.Left, source.Top)); Control(new(source.Right, source.Top));
                Control(new(source.Left, source.Bottom)); Control(new(source.Right, source.Bottom));
            }
            else if (reader.LocalName == "path")
            {
                using SvgRenderablePathData data = SvgRenderablePathData.Read(reader.GetAttribute("d"), maximumCommands, state.Budget, token);
                using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Parser, token);
                SvgPathDataParser.ParseCommands(data.Value, maximumCommands, token, commands);
                int count = 0;
                for (int index = 0; index < commands.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (commands[index].Verb == SvgAuthoredPathVerb.Move && index + 1 < commands.Count
                        && commands[index + 1].Verb == SvgAuthoredPathVerb.End)
                    { index++; continue; }
                    commands.AsMutableSpan()[count++] = commands[index];
                }
                while (commands.Count > count) commands.RemoveLast();
                SvgRectangle bounds = SvgPathBounds.Compute(commands.AsSpan(), token);
                Union(Box(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                SvgDevicePoint current = default, first = default;
                int moves = 0, segments = 0;
                bool onlyLines = true, closed = false;
                Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
                foreach (SvgAuthoredPathCommand command in commands.AsSpan())
                {
                    token.ThrowIfCancellationRequested();
                    switch (command.Verb)
                    {
                        case SvgAuthoredPathVerb.Move:
                            current = first = command.First; moves++; Control(current); break;
                        case SvgAuthoredPathVerb.Line:
                            current = command.First; segments++; Control(current); break;
                        case SvgAuthoredPathVerb.Quadratic:
                        case SvgAuthoredPathVerb.Conic:
                            Control(command.First); Control(command.Second); current = command.Second; onlyLines = false; break;
                        case SvgAuthoredPathVerb.Cubic:
                            Control(command.First); Control(command.Second); Control(command.Third); current = command.Third; onlyLines = false; break;
                        case SvgAuthoredPathVerb.Arc:
                            token.ThrowIfCancellationRequested();
                            int conics = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                                (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                                command.Third, arcs, token);
                            for (int index = 0; index < conics; index++) { Control(arcs[index].Control); Control(arcs[index].End); }
                            if (conics != 0) onlyLines = false;
                            else if (current != command.Third) segments++;
                            current = command.Third; Control(current); break;
                        case SvgAuthoredPathVerb.Close: closed = true; current = first; break;
                        case SvgAuthoredPathVerb.End: current = first; break;
                    }
                }
                line = moves == 1 && segments == 1 && onlyLines && !closed;
            }
            else if (reader.LocalName is "polygon" or "polyline")
            {
                using BudgetedParserList<SvgFractionalPoint> points = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(reader.GetAttribute("points") ?? string.Empty,
                    reader.LocalName, maximumCommands, state.Budget, token, 0);
                float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                foreach (SvgFractionalPoint point in points.AsSpan())
                {
                    token.ThrowIfCancellationRequested();
                    left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
                    right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
                    Control(new(point.X, point.Y));
                }
                if (points.Count != 0) Union(Box(left, top, right - left, bottom - top));
                line = reader.LocalName == "polyline" && points.Count == 2;
            }
            if (paint.StrokeBoundsInflation > 0 && !float.IsPositiveInfinity(controlLeft) && host.TryInvert(out SvgAffineTransform hostInverse))
            {
                float inflation = (float)SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width ?? reader.GetAttribute("stroke-width"), reader.LocalName) * .5f;
                if (reader.LocalName is "path" or "polygon" or "polyline" && !line) inflation = paint.StrokeBoundsInflation;
                else if (line && SvgStrokeStyleSyntax.ParseLinecap(style.Linecap) == SvgStrokeCap.Square) inflation *= MathF.Sqrt(2);
                SvgClipBox controls = Box(controlLeft, controlTop, controlRight - controlLeft, controlBottom - controlTop);
                SvgClipBox stroke = Box((float)controls.X - inflation, (float)controls.Y - inflation,
                    (float)controls.Width + 2 * inflation, (float)controls.Height + 2 * inflation);
                Union(Map(stroke, hostInverse, false));
            }
            foreach (SvgGeometry geometry in parsed.Geometries)
            {
                token.ThrowIfCancellationRequested();
                SvgAuthoredPath? path = geometry.AuthoredPath?.StrokeOutline ?? geometry.AuthoredPath;
                SvgRectangle bounds = path is null ? SvgMaskSyntax.ResourceBounds(geometry, token) : SvgPathBounds.Compute(path.Commands, token);
                SvgClipBox box = Box(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                if (path is not null) box = Map(box, path.SceneMap, false);
                Union(Map(box, inverse, false));
            }
            if (visual.Width > 0 && visual.Height > 0)
            {
                if (picture) return parsed;
                // SVG drawing records enclose in bounded integer coordinates before
                // the property-tree map and viewport intersection. Keep the size
                // separate from the origin through the native axis-map fast path.
                (double left, double width) = Enclose(visual.X, visual.Width);
                (double top, double height) = Enclose(visual.Y, visual.Height);
                SvgClipBox mapped = Map(new(left, top, width, height), host, true);
                if (state.OutputClip is {} clip)
                {
                    float x = MathF.Max((float)mapped.X, (float)clip.X), y = MathF.Max((float)mapped.Y, (float)clip.Y);
                    float right = MathF.Min((float)mapped.X + (float)mapped.Width, (float)clip.Right);
                    float bottom = MathF.Min((float)mapped.Y + (float)mapped.Height, (float)clip.Bottom);
                    mapped = Box(x, y, right - x, bottom - y);
                }
                if (mapped.Width > 0 && mapped.Height > 0) return parsed;
            }
            for (int index = 0; index < parsed.Storage.Items.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgGeometry geometry = parsed.Storage.Items[index];
                SvgRecordedPaint recorded = geometry.RecordedPaint;
                if (geometry.RecordedPaint.Identity > 0)
                    state.Scene.ChangeRecordedPaint(geometry.RecordedPaint, state.Scene.GetRecordedPaintValue(geometry.RecordedPaint)
                        with { IsEffectAnchor = true, VisualBounds = default(SvgPaintBounds) });
                else
                {
                    SvgRectangle box = SvgMaskSyntax.ResourceBounds(geometry, token);
                    SvgRecordedPaintValue inert = new(state.NewPaintOperationIdentity(), 0, false, paint.Scope, paint.ElementOpacity, paint.OpacityScope)
                    { IsEffectAnchor = true, IsMarkerContent = true, ObjectBounds = new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)), VisualBounds = default(SvgPaintBounds) };
                    recorded = state.Scene.RegisterRecordedPaint(inert);
                }
                // A culled marker retains no inner drawing scopes that could clip an ancestor effect.
                parsed.Storage.Items[index] = SvgGeometry.FromRectangle(default) with { RecordedPaint = recorded, LeafMask = state.Scene.GetRecordedPaintValue(recorded).IsMarkerContent ? null : geometry.LeafMask };
                geometry.Dispose();
            }
            return parsed;

            void Union(SvgClipBox box)
            {
                if (box.Width == 0 || box.Height == 0) return;
                if (visual.Width == 0 || visual.Height == 0) { visual = box; return; }
                float x = MathF.Min((float)visual.X, (float)box.X), y = MathF.Min((float)visual.Y, (float)box.Y);
                visual = Box(x, y, MathF.Max((float)visual.Right, (float)box.Right) - x, MathF.Max((float)visual.Bottom, (float)box.Bottom) - y);
            }
            void Control(SvgDevicePoint point)
            {
                token.ThrowIfCancellationRequested();
                SvgDevicePoint mapped = SvgAuthoredPath.MapPoint(host, point, new(0, 0));
                controlLeft = MathF.Min(controlLeft, (float)mapped.X); controlTop = MathF.Min(controlTop, (float)mapped.Y);
                controlRight = MathF.Max(controlRight, (float)mapped.X); controlBottom = MathF.Max(controlBottom, (float)mapped.Y);
            }
        }
        catch { parsed.Discard(); throw; }

        static SvgClipBox Box(double x, double y, double width, double height)
        {
            // gfx::SizeF uses eight float epsilons, independently of scale.
            const float trivial = 8f / 8388608f;
            float w = Scalar(width), h = Scalar(height);
            return new(Scalar(x), Scalar(y), w > trivial ? w : 0, h > trivial ? h : 0);
        }
        static float Scalar(double value) => (float)Math.Clamp(value, -float.MaxValue, float.MaxValue);
        static SvgClipBox Map(SvgClipBox box, SvgAffineTransform map, bool axis)
        {
            if (axis && map.IsAxisAligned && map.A >= 0 && map.D >= 0)
                return Box((float)box.X * (float)map.A + (float)map.E, (float)box.Y * (float)map.D + (float)map.F,
                    (float)box.Width * (float)map.A, (float)box.Height * (float)map.D);
            float x1 = Scalar(map.A * box.X + map.C * box.Y + map.E), y1 = Scalar(map.B * box.X + map.D * box.Y + map.F);
            float x2 = Scalar(map.A * box.Right + map.C * box.Y + map.E), y2 = Scalar(map.B * box.Right + map.D * box.Y + map.F);
            float x3 = Scalar(map.A * box.X + map.C * box.Bottom + map.E), y3 = Scalar(map.B * box.X + map.D * box.Bottom + map.F);
            float x4 = Scalar(map.A * box.Right + map.C * box.Bottom + map.E), y4 = Scalar(map.B * box.Right + map.D * box.Bottom + map.F);
            float left = MathF.Min(MathF.Min(x1, x2), MathF.Min(x3, x4)), top = MathF.Min(MathF.Min(y1, y2), MathF.Min(y3, y4));
            return Box(left, top, MathF.Max(MathF.Max(x1, x2), MathF.Max(x3, x4)) - left, MathF.Max(MathF.Max(y1, y2), MathF.Max(y3, y4)) - top);
        }
        static (double Origin, double Size) Enclose(double origin, double size)
        {
            double first = Math.Clamp(Math.Floor(origin), int.MinValue, int.MaxValue);
            double last = Math.Clamp(Math.Ceiling((float)origin + (float)size), int.MinValue, int.MaxValue);
            double width = Math.Min(int.MaxValue, last - first);
            if (last - first <= int.MaxValue) return (first, width);
            double limit = int.MaxValue / 2;
            if (Math.Abs(last) < limit) return (last - width, width);
            if (Math.Abs(first) < limit) return (first, width);
            return (first + Math.Floor(Math.Min(int.MaxValue, last - first - width) / 2), width);
        }
    }
}
