// Adapts src/utils/SkDashPath.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2014 Google Inc.; 2018 Google LLC.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains a source-space construction clip, already expanded by the stroke record.</summary>
internal readonly record struct SvgDashCull(float Left, float Top, float Right, float Bottom);

/// <summary>Culls native line/rectangle sources before dash measurement, preserving their phase.</summary>
/// <remarks>Input spans remain borrowed and immutable. Output storage is reserved before allocation
/// and transfers only on success. Each source command and emitted segment observes cancellation.
/// Ineligible or unchanged lines return no owner. Calls share no mutable state.</remarks>
internal static class SvgDashCulling
{
    internal static bool IsEligible(ReadOnlySpan<SvgAuthoredPathCommand> commands, SvgAffineTransform map, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsLine(commands))
        {
            SvgDevicePoint first = Map(map, commands[0].First), last = Map(map, commands[1].First);
            return first.X == last.X || first.Y == last.Y;
        }
        SvgRectanglePathRecognizer rectangle = new();
        foreach (SvgAuthoredPathCommand command in commands)
        {
            token.ThrowIfCancellationRequested();
            if (!rectangle.Add(command.Verb, command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line
                ? Map(map, command.First) : default)) return false;
        }
        return rectangle.Finish() is not null;
    }

    internal static OwnedParserArray<SvgAuthoredPathCommand> Cull(ReadOnlySpan<SvgAuthoredPathCommand> commands,
        SvgAffineTransform map, SvgDashCull bounds, float period, float phase, ReadOnlySpan<float> pattern,
        int initialIndex, float originalLength, RenderBudget budget, CancellationToken token, out bool changed)
    {
        token.ThrowIfCancellationRequested();
        changed = false;
        if (!IsEligible(commands, map, token)) return new([], null);
        using RenderBudget.BudgetReservation owner = budget.Reserve(128, SvgMemoryCategory.Geometry);
        using BudgetedParserList<SvgAuthoredPathCommand> output = new(budget, SvgMemoryCategory.Geometry, token);
        if (IsLine(commands))
        {
            SvgDevicePoint first = Map(map, commands[0].First), last = Map(map, commands[1].First);
            SvgDevicePoint start = first, stop = last;
            if (!ClipLine(ref start, ref stop, bounds, period, 0) || start == first && stop == last) return new([], null);
            output.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, start, default, default));
            output.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, stop, default, default));
            output.Add(new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
        }
        else
        {
            SvgDevicePoint first = default, current = default, lastOutput = default;
            SvgDevicePoint incoming = default, outgoing = default;
            bool active = false, closed = false, hasOutgoing = false, hasSourceLine = false;
            // Native culling iterates the first contour; join repair separately
            // asks whether the complete source's final verb closes a contour.
            foreach (SvgAuthoredPathCommand command in commands)
            {
                token.ThrowIfCancellationRequested();
                if (command.Verb == SvgAuthoredPathVerb.Close) closed = true;
                else if (command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line) closed = false;
            }
            double accumulated = 0;
            foreach (SvgAuthoredPathCommand command in commands)
            {
                token.ThrowIfCancellationRequested();
                if (command.Verb == SvgAuthoredPathVerb.Move)
                {
                    if (hasSourceLine) break;
                    current = first = Map(map, command.First);
                    continue;
                }
                if (command.Verb == SvgAuthoredPathVerb.End) { if (hasSourceLine) break; continue; }
                if (command.Verb is not (SvgAuthoredPathVerb.Line or SvgAuthoredPathVerb.Close)) continue;
                SvgDevicePoint next = command.Verb == SvgAuthoredPathVerb.Close ? first : Map(map, command.First);
                if (command.Verb == SvgAuthoredPathVerb.Close && next == current) break;
                hasSourceLine = true;
                if (next != first) incoming = next;
                if (!hasOutgoing && next != first) { outgoing = next; hasOutgoing = true; }
                SvgDevicePoint start = current, stop = next;
                if (ClipLine(ref start, ref stop, bounds, period, (float)(accumulated % period)))
                {
                    if (!active || lastOutput != start)
                    {
                        if (active) output.Add(new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
                        output.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, start, default, default));
                        active = true;
                    }
                    output.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, stop, default, default));
                    lastOutput = stop;
                }
                accumulated += MathF.Abs(((float)next.X - (float)current.X) + ((float)next.Y - (float)current.Y));
                current = next;
                if (command.Verb == SvgAuthoredPathVerb.Close) break;
            }
            if (!active) return new([], null);
            output.Add(new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
            if (closed && (initialIndex & 1) == 0)
            {
                float endPhase = (originalLength + phase) % period;
                int index = 0;
                while (endPhase > pattern[index])
                {
                    token.ThrowIfCancellationRequested();
                    endPhase -= pattern[index++];
                    if (index == pattern.Length) { endPhase = 0; break; }
                }
                if (((index & 1) == 0) == (endPhase > 0))
                {
                    const float tiny = 1f / (1 << 12);
                    SvgDevicePoint before = new((float)first.X - ((float)first.X - (float)incoming.X) * tiny,
                        (float)first.Y - ((float)first.Y - (float)incoming.Y) * tiny);
                    SvgDevicePoint after = new((float)first.X - ((float)first.X - (float)outgoing.X) * tiny,
                        (float)first.Y - ((float)first.Y - (float)outgoing.Y) * tiny);
                    output.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, before, default, default));
                    output.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, first, default, default));
                    output.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, after, default, default));
                    output.Add(new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
                }
            }
        }
        changed = true;
        return output.Detach();
    }

    internal static bool ClipLine(ref SvgDevicePoint first, ref SvgDevicePoint last, SvgDashCull bounds, float period, float priorPhase)
    {
        float dx = (float)last.X - (float)first.X, dy = (float)last.Y - (float)first.Y;
        if (dx != 0 && dy != 0) return false;
        bool vertical = dy != 0;
        float minimum = vertical ? (float)first.Y : (float)first.X;
        float maximum = vertical ? (float)last.Y : (float)last.X;
        bool swapped = maximum < minimum;
        if (swapped) (minimum, maximum) = (maximum, minimum);
        float low = vertical ? bounds.Top : bounds.Left, high = vertical ? bounds.Bottom : bounds.Right;
        if (maximum < low || minimum > high) return false;
        if (minimum < low) { minimum = low - (low - minimum) % period; if (!swapped) minimum -= priorPhase; }
        if (maximum > high) { maximum = high + (maximum - high) % period; if (swapped) maximum += priorPhase; }
        if (swapped) (minimum, maximum) = (maximum, minimum);
        if (vertical) { first = first with { Y = minimum }; last = last with { Y = maximum }; }
        else { first = first with { X = minimum }; last = last with { X = maximum }; }
        if (first == last) last = last with { X = (float)last.X + MathF.Max(1.001f, (float)last.X) * (1f / (1 << 12)) };
        return true;
    }

    private static bool IsLine(ReadOnlySpan<SvgAuthoredPathCommand> commands)
        => commands.Length == 3 && commands[0].Verb == SvgAuthoredPathVerb.Move
            && commands[1].Verb == SvgAuthoredPathVerb.Line && commands[2].Verb == SvgAuthoredPathVerb.End;

    internal static SvgDevicePoint Map(SvgAffineTransform map, SvgDevicePoint point)
    {
        float x = (float)point.X, y = (float)point.Y;
        return new(((float)map.A * x + (float)map.C * y) + (float)map.E,
            ((float)map.B * x + (float)map.D * y) + (float)map.F);
    }
}
