using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses bounded absolute integer path and point-list contours into charged point storage. Diagonal contours materialize here; orthogonality is enforced downstream at lowering.</summary>
internal static class SvgOrthogonalGeometryTokenizer
{
    internal static BudgetedParserList<SvgPoint> ParsePath(string value, int maximumGeometryElements, RenderBudget budget, CancellationToken cancellationToken, BudgetedParserList<int> contourEnds, bool allowDegenerateContours)
    {
        ArgumentNullException.ThrowIfNull(contourEnds);
        ValidatePathCommandCount(value, maximumGeometryElements, cancellationToken);
        PathTokenReader tokens = new(value, cancellationToken);
        if (tokens.End) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
        char firstCommand = tokens.ReadCommand();
        if (firstCommand is not ('M' or 'm')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
        BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, cancellationToken);
        try
        {
            int commandIndex = 0;
            SvgPoint current = new(0, 0);
            bool hasMore = true;
            bool firstMove = true;
            char moveCommand = firstCommand;
            bool justClosed = false;
            while (hasMore)
            {
                bool continuedContour = false;
                if (!firstMove)
                {
                    if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    moveCommand = tokens.ReadCommand();
                    if (justClosed && moveCommand is 'Z' or 'z')
                    {
                        // Another close leaves the current point and sealed contour unchanged.
                        // Authored commands remain available to stroke/marker parsing and limits.
                        hasMore = !tokens.End;
                        continue;
                    }
                    if (moveCommand is 'M' or 'm') justClosed = false;
                    else if (justClosed && moveCommand is 'L' or 'l' or 'H' or 'h' or 'V' or 'v') continuedContour = true;
                    else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                }
                firstMove = false;
                bool closedExplicit = false;
                int contourStart;
                char implicitLine;
                char? pendingSegment = null;
                if (continuedContour)
                {
                    // A z-continued contour resumes at the closed subpath start, matching the general path parser.
                    justClosed = false;
                    points.Add(current);
                    contourStart = points.Count - 1;
                    implicitLine = moveCommand;
                    pendingSegment = moveCommand;
                }
                else
                {
                    int dx = tokens.ReadInteger();
                    int dy = tokens.ReadInteger();
                    current = moveCommand == 'M' ? new SvgPoint(dx, dy) : AddOffset(current, dx, dy);
                    points.Add(current);
                    contourStart = points.Count - 1;
                    implicitLine = moveCommand == 'M' ? 'L' : 'l';
                    while (tokens.PeekIsValueStart() && !tokens.End)
                    {
                        if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        int ix = tokens.ReadInteger();
                        int iy = tokens.ReadInteger();
                        current = AddLinePoint(points, contourStart, current, implicitLine == 'L' ? new SvgPoint(ix, iy) : AddOffset(current, ix, iy));
                    }
                }
                while (!tokens.End)
                {
                    if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    char command;
                    if (pendingSegment is char resumed)
                    {
                        command = resumed;
                        pendingSegment = null;
                    }
                    else
                    {
                        if (tokens.PeekIsValueStart())
                        {
                            int ix = tokens.ReadInteger();
                            int iy = tokens.ReadInteger();
                            current = AddLinePoint(points, contourStart, current, implicitLine == 'L' ? new SvgPoint(ix, iy) : AddOffset(current, ix, iy));
                            continue;
                        }
                        // An unclosed subpath seals implicitly for fill when the next subpath begins; the outer loop consumes the moveto.
                        if (tokens.PeekCommand() is 'M' or 'm') break;
                        command = tokens.ReadCommand();
                    }
                    if (command == 'H' || command == 'h')
                    {
                        do
                        {
                            int hx = tokens.ReadInteger();
                            current = AddLinePoint(points, contourStart, current, command == 'H' ? new SvgPoint(hx, current.Y) : AddOffset(current, hx, 0));
                        } while (tokens.PeekIsValueStart() && !tokens.End);
                        implicitLine = command == 'H' ? 'H' : 'h';
                    }
                    else if (command == 'V' || command == 'v')
                    {
                        do
                        {
                            int vy = tokens.ReadInteger();
                            current = AddLinePoint(points, contourStart, current, command == 'V' ? new SvgPoint(current.X, vy) : AddOffset(current, 0, vy));
                        } while (tokens.PeekIsValueStart() && !tokens.End);
                        implicitLine = command == 'V' ? 'V' : 'v';
                    }
                    else if (command == 'L' || command == 'l')
                    {
                        do
                        {
                            int lx = tokens.ReadInteger();
                            int ly = tokens.ReadInteger();
                            current = AddLinePoint(points, contourStart, current, command == 'L' ? new SvgPoint(lx, ly) : AddOffset(current, lx, ly));
                        } while (tokens.PeekIsValueStart() && !tokens.End);
                        implicitLine = command;
                    }
                    else if (command is 'Z' or 'z') { closedExplicit = true; justClosed = true; break; }
                    else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                }
                if (pendingSegment is not null) throw new SvgException(SvgErrorCode.MalformedInput, "Path data ends inside a command.");
                // A closepath moves the current point back to the subpath start; open ends stay in place.
                if (closedExplicit) current = points[contourStart];
                // An explicit return to the contour start without closepath seals the contour for fill, keeping single-point contours for stroke outlining.
                if (points.Count > contourStart + 1 && points[points.Count - 1] == points[contourStart]) points.RemoveLast();
                int contourEnd = points.Count;
                if (contourEnd - contourStart < 3 && (!allowDegenerateContours || contourEnd == contourStart)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                if (points.Count > maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
                for (int pointIndex = contourStart; pointIndex < contourEnd && contourEnd - contourStart > 1; pointIndex++)
                {
                    SvgPoint start = points[pointIndex]; SvgPoint end = pointIndex + 1 < contourEnd ? points[pointIndex + 1] : points[contourStart];
                    if (start.X == end.X && start.Y == end.Y) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                }
                contourEnds.Add(contourEnd);
                if (tokens.End) hasMore = false;
            }
            if (contourEnds.Count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
            return points;
        }
        catch
        {
            points.Dispose();
            throw;
        }
    }

    private static SvgPoint AddOffset(SvgPoint current, int dx, int dy)
    {
        try { return new SvgPoint(checked(current.X + dx), checked(current.Y + dy)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer path exceeds the coordinate range."); }
    }

    private static SvgPoint AddLinePoint(BudgetedParserList<SvgPoint> points, int contourStart, SvgPoint current, SvgPoint next)
    {
        if (next.X == current.X && next.Y == current.Y) return current;
        if (points.Count - contourStart >= 2)
        {
            SvgPoint previous = points[points.Count - 2];
            if (previous.Y == current.Y && current.Y == next.Y)
            {
                long step = (long)current.X - previous.X;
                long advance = (long)next.X - current.X;
                if ((advance > 0 && step > 0) || (advance < 0 && step < 0))
                {
                    points[points.Count - 1] = next;
                    return next;
                }
            }
            else if (previous.X == current.X && current.X == next.X)
            {
                long step = (long)current.Y - previous.Y;
                long advance = (long)next.Y - current.Y;
                if ((advance > 0 && step > 0) || (advance < 0 && step < 0))
                {
                    points[points.Count - 1] = next;
                    return next;
                }
            }
        }
        points.Add(next);
        return next;
    }
    internal static BudgetedParserList<SvgPoint> ParsePointList(string value, string elementName, int maximumGeometryElements, RenderBudget budget, CancellationToken cancellationToken, bool allowDegenerateContours)
    {
        ValidatePointListVertexCount(value, maximumGeometryElements, cancellationToken);
        BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, cancellationToken);
        try
        {
            DelimitedTokenReader values = new(value, cancellationToken);
            int vertexCount = 0;
            while (values.TryReadValue(out ReadOnlySpan<char> x))
            {
                if ((vertexCount & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryReadValue(out ReadOnlySpan<char> y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
                if (vertexCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
                points.Add(new SvgPoint(IntegerPoint(x, elementName), IntegerPoint(y, elementName)));
                vertexCount++;
            }
            if (points.Count < 3 && (!allowDegenerateContours || points.Count < 2 || points[0] == points[1])) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
            for (int index = 0; index < points.Count; index++)
            {
                SvgPoint start = points[index]; SvgPoint end = points[(index + 1) % points.Count];
                if (start.X == end.X && start.Y == end.Y) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
            }
            return points;
        }
        catch
        {
            points.Dispose();
            throw;
        }

        static int IntegerPoint(ReadOnlySpan<char> value, string elementName)
        {
            double number = ParsePointCoordinate(value, elementName);
            if (number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, elementName + " point coordinates must be bounded integers on this route.");
            return (int)number;
        }
    }

    internal static BudgetedParserList<SvgFractionalPoint> ParseFractionalPointList(string value, string elementName, int maximumGeometryElements, RenderBudget budget, CancellationToken cancellationToken)
        => ParseFractionalPointList(value, elementName, maximumGeometryElements, budget, cancellationToken, 3);

    internal static BudgetedParserList<SvgFractionalPoint> ParseFractionalPointList(string value, string elementName, int maximumGeometryElements, RenderBudget budget, CancellationToken cancellationToken, int minimumPoints)
    {
        ValidatePointListVertexCount(value, maximumGeometryElements, cancellationToken);
        BudgetedParserList<SvgFractionalPoint> points = new(budget, SvgMemoryCategory.Geometry, cancellationToken);
        try
        {
            DelimitedTokenReader values = new(value, cancellationToken);
            int vertexCount = 0;
            bool invalidNativeCoordinate = false;
            while (values.TryReadValue(out ReadOnlySpan<char> x))
            {
                if ((vertexCount & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryReadValue(out ReadOnlySpan<char> y)) throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points must pair every x coordinate with a y coordinate.");
                if (vertexCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
                float px = NativeCoordinate(x, elementName, cancellationToken);
                float py = NativeCoordinate(y, elementName, cancellationToken);
                invalidNativeCoordinate |= !float.IsFinite(px) || !float.IsFinite(py);
                if (!invalidNativeCoordinate) points.Add(new SvgFractionalPoint(px, py));
                vertexCount++;
            }
            if (vertexCount < minimumPoints) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The " + elementName + " has too few vertices for its geometry consumer.");
            // A failed native number invalidates the complete SVG point list,
            // including a valid prefix. Still validate all authored syntax/limits.
            if (invalidNativeCoordinate) points.Clear();
            return points;
        }
        catch
        {
            points.Dispose();
            throw;
        }

        static float NativeCoordinate(ReadOnlySpan<char> token, string name, CancellationToken cancellation)
        {
            _ = ParsePointCoordinate(token, name);
            float native = SvgFloatNumber.ReadValidated(token, cancellation);
            // SVG numbers reject positive exponents above the float decimal range,
            // even for zero; other numeric consumers retain their own rules.
            int exponentAt = token.IndexOfAny('e', 'E');
            return exponentAt >= 0 && double.Parse(token[(exponentAt + 1)..], SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture) > 38
                ? float.NaN : native;
        }
    }

    private static double ParsePointCoordinate(ReadOnlySpan<char> token, string elementName)
    {
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " point values exceed the configured syntax limit.");
        if (!double.TryParse(token, SvgFloatNumber.TokenStyles, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.MalformedInput, elementName + " point coordinates must be finite bare numbers.");
        return result;
    }

    private static void ValidatePathCommandCount(string value, int maximumGeometryElements, CancellationToken cancellationToken)
    {
        int commandCount = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (value[index] is not ('M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z')) continue;
            if (commandCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
            commandCount++;
        }
    }

    private static void ValidatePointListVertexCount(string value, int maximumGeometryElements, CancellationToken cancellationToken)
    {
        long maximumValues = (long)maximumGeometryElements * 2;
        int valueCount = 0;
        bool withinValue = false;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (value[index] is ',' or ' ' or '\t' or '\r' or '\n') { withinValue = false; continue; }
            if (withinValue) continue;
            if (valueCount >= maximumValues) throw new SvgException(SvgErrorCode.LimitExceeded, "Point-list vertex count exceeds the configured limit.");
            valueCount++;
            withinValue = true;
        }
    }

    private static bool IsPathInteger(ReadOnlySpan<char> value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Path coordinate values exceed the configured syntax limit.");
        if (value.IsEmpty) return false;
        int start = value[0] is '-' or '+' ? 1 : 0;
        if (start == value.Length) return false;
        for (int index = start; index < value.Length; index++) if (!char.IsAsciiDigit(value[index])) return false;
        return true;
    }

    private static bool IsPathCommand(char value) => value is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z';

    private static bool IsPathSeparator(char value) => value is ',' or ' ' or '\t' or '\r' or '\n';

    private static bool IsPointSeparator(char value) => value is ',' or ' ' or '\t' or '\r' or '\n';

    private ref struct PathTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly CancellationToken _cancellationToken;
        private int _position;
        private int _nextCheckpoint;

        internal PathTokenReader(ReadOnlySpan<char> value, CancellationToken cancellationToken)
        {
            _value = value;
            _cancellationToken = cancellationToken;
        }

        internal bool End
        {
            get
            {
                SkipSeparators();
                ConsumeCheckpoints();
                return _position == _value.Length;
            }
        }

        internal char ReadCommand()
        {
            SkipSeparators();
            ConsumeCheckpoints();
            if (_position == _value.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
            return _value[_position++];
        }

        internal char PeekCommand()
        {
            SkipSeparators();
            ConsumeCheckpoints();
            if (_position == _value.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
            return _value[_position];
        }

        internal ReadOnlySpan<char> ReadValue()
        {
            SkipSeparators();
            ConsumeCheckpoints();
            int start = _position;
            while (_position < _value.Length && !IsPathSeparator(_value[_position]) && !IsPathCommand(_value[_position]))
            {
                // A sign after a digit starts the next number in compact exporter output like 99-99.
                if (_position > start && _value[_position] is '-' or '+' && char.IsAsciiDigit(_value[_position - 1])) break;
                _position++;
                ConsumeCheckpoints();
            }
            if (start == _position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
            return _value[start.._position];
        }

        internal int ReadInteger()
        {
            ReadOnlySpan<char> value = ReadValue();
            if (!IsPathInteger(value)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
            return SvgGeometrySyntax.ParseInteger(value);
        }

        internal bool PeekIsValueStart()
        {
            int position = _position;
            while (position < _value.Length && IsPathSeparator(_value[position])) position++;
            if (position == _value.Length) return false;
            char next = _value[position];
            return char.IsAsciiDigit(next) || next is '-' or '+' or '.';
        }

        private void SkipSeparators()
        {
            while (_position < _value.Length && IsPathSeparator(_value[_position])) { ConsumeCheckpoints(); _position++; }
        }

        private void ConsumeCheckpoints()
        {
            while (_nextCheckpoint < _value.Length && _position >= _nextCheckpoint)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _nextCheckpoint += 4096;
            }
        }
    }

    /// <summary>Streams borrowed point tokens with cancellation checkpoints, owning no storage.</summary>
    internal ref struct DelimitedTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly CancellationToken _cancellationToken;
        private int _position;
        private int _nextCheckpoint;
        private bool _hasValue;

        internal DelimitedTokenReader(ReadOnlySpan<char> value, CancellationToken cancellationToken)
        {
            _value = value;
            _cancellationToken = cancellationToken;
        }

        internal bool TryReadValue(out ReadOnlySpan<char> value)
        {
            while (_position < _value.Length && _value[_position] != ',' && IsPointSeparator(_value[_position])) { ConsumeCheckpoints(); _position++; }
            if (_position < _value.Length && _value[_position] == ',')
            {
                if (!_hasValue) throw new SvgException(SvgErrorCode.MalformedInput, "Point-list commas require a preceding number.");
                _position++;
                while (_position < _value.Length && _value[_position] != ',' && IsPointSeparator(_value[_position])) { ConsumeCheckpoints(); _position++; }
                if (_position == _value.Length || _value[_position] == ',')
                    throw new SvgException(SvgErrorCode.MalformedInput, "Point-list commas require a following number.");
            }
            ConsumeCheckpoints();
            if (_position == _value.Length) { value = default; return false; }
            int start = _position;
            while (_position < _value.Length && !IsPointSeparator(_value[_position])) { _position++; ConsumeCheckpoints(); }
            value = _value[start.._position];
            _hasValue = true;
            return true;
        }

        private void ConsumeCheckpoints()
        {
            while (_nextCheckpoint < _value.Length && _position >= _nextCheckpoint)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                _nextCheckpoint += 4096;
            }
        }
    }
}
