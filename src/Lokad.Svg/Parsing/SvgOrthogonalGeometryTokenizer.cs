using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses bounded absolute integer path and point-list contours into charged point storage. Diagonal contours materialize here; orthogonality is enforced downstream at lowering.</summary>
internal static class SvgOrthogonalGeometryTokenizer
{
    internal static BudgetedParserList<SvgPoint> ParsePath(string value, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, BudgetedParserList<int> contourEnds, bool allowDegenerateContours)
    {
        ArgumentNullException.ThrowIfNull(contourEnds);
        ValidatePathCommandCount(value, maximumGeometryElements, workCounter, cancellationToken);
        PathTokenReader tokens = new(value, workCounter, cancellationToken);
        if (tokens.End || tokens.ReadCommand() is not ('M' or 'm')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
        BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        try
        {
            int commandIndex = 0;
            SvgPoint current = new(0, 0);
            bool hasMore = true;
            bool firstMove = true;
            char moveCommand = 'M';
            while (hasMore)
            {
                if (!firstMove)
                {
                    if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    moveCommand = tokens.ReadCommand();
                    if (moveCommand is not ('M' or 'm')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                }
                firstMove = false;
                bool closedExplicit = false;
                int dx = tokens.ReadInteger();
                int dy = tokens.ReadInteger();
                current = moveCommand == 'M' ? new SvgPoint(dx, dy) : AddOffset(current, dx, dy);
                points.Add(current);
                int contourStart = points.Count - 1;
                char implicitLine = moveCommand == 'M' ? 'L' : 'l';
                while (tokens.PeekIsValueStart() && !tokens.End)
                {
                    if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int ix = tokens.ReadInteger();
                    int iy = tokens.ReadInteger();
                    current = AddLinePoint(points, contourStart, current, implicitLine == 'L' ? new SvgPoint(ix, iy) : AddOffset(current, ix, iy));
                }
                while (!tokens.End)
                {
                    if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (tokens.PeekIsValueStart())
                    {
                        int ix = tokens.ReadInteger();
                        int iy = tokens.ReadInteger();
                        current = AddLinePoint(points, contourStart, current, implicitLine == 'L' ? new SvgPoint(ix, iy) : AddOffset(current, ix, iy));
                        continue;
                    }
                    // An unclosed subpath seals implicitly for fill when the next subpath begins; the outer loop consumes the moveto.
                    if (tokens.PeekCommand() is 'M' or 'm') break;
                    char command = tokens.ReadCommand();
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
                    else if (command is 'Z' or 'z') { closedExplicit = true; break; }
                    else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                }
                // A closepath moves the current point back to the subpath start; open ends stay in place.
                if (closedExplicit) current = points[contourStart];
                // An explicit return to the contour start without closepath seals the contour for fill.
                if (points.Count > contourStart && points[points.Count - 1] == points[contourStart]) points.RemoveLast();
                int contourEnd = points.Count;
                if (contourEnd - contourStart < 3 && (!allowDegenerateContours || contourEnd - contourStart < 2 || points[contourStart] == points[contourStart + 1])) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer paths are implemented.");
                if (points.Count > maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
                for (int pointIndex = contourStart; pointIndex < contourEnd; pointIndex++)
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
    internal static BudgetedParserList<SvgPoint> ParsePointList(string value, string elementName, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowDegenerateContours)
    {
        ValidatePointListVertexCount(value, maximumGeometryElements, workCounter, cancellationToken);
        BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        try
        {
            DelimitedTokenReader values = new(value, workCounter, cancellationToken);
            int vertexCount = 0;
            while (values.TryReadValue(out ReadOnlySpan<char> x))
            {
                if ((vertexCount & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryReadValue(out ReadOnlySpan<char> y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
                if (vertexCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
                points.Add(new SvgPoint(SvgGeometrySyntax.ParseInteger(x), SvgGeometrySyntax.ParseInteger(y)));
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
    }

    internal static BudgetedParserList<SvgFractionalPoint> ParseFractionalPointList(string value, string elementName, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ValidatePointListVertexCount(value, maximumGeometryElements, workCounter, cancellationToken);
        BudgetedParserList<SvgFractionalPoint> points = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        try
        {
            DelimitedTokenReader values = new(value, workCounter, cancellationToken);
            int vertexCount = 0;
            while (values.TryReadValue(out ReadOnlySpan<char> x))
            {
                if ((vertexCount & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryReadValue(out ReadOnlySpan<char> y)) throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points must pair every x coordinate with a y coordinate.");
                if (vertexCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
                points.Add(new SvgFractionalPoint(ParseFractionalCoordinate(x, elementName), ParseFractionalCoordinate(y, elementName)));
                vertexCount++;
            }
            if (points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed fractional " + elementName + " contours are implemented.");
            return points;
        }
        catch
        {
            points.Dispose();
            throw;
        }
    }

    private static double ParseFractionalCoordinate(ReadOnlySpan<char> value, string elementName)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " point values exceed the configured syntax limit.");
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.MalformedInput, elementName + " point coordinates must be finite numbers.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.MalformedInput, elementName + " point coordinates must be finite numbers.");
        return result;
    }

    private static void ValidatePathCommandCount(string value, int maximumGeometryElements, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int commandCount = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
            if (value[index] is not ('M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z')) continue;
            if (commandCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
            commandCount++;
        }
    }

    private static void ValidatePointListVertexCount(string value, int maximumGeometryElements, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        long maximumValues = (long)maximumGeometryElements * 2;
        int valueCount = 0;
        bool withinValue = false;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
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
        int start = value[0] == '-' ? 1 : 0;
        if (start == value.Length) return false;
        for (int index = start; index < value.Length; index++) if (!char.IsAsciiDigit(value[index])) return false;
        return true;
    }

    private static bool IsPathCommand(char value) => value is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z';

    private static bool IsPathSeparator(char value) => value == ',' || char.IsWhiteSpace(value);

    private static bool IsPointSeparator(char value) => value is ',' or ' ' or '\t' or '\r' or '\n';

    private ref struct PathTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly ParserWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private int _position;
        private int _nextCheckpoint;

        internal PathTokenReader(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _value = value;
            _workCounter = workCounter;
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
            while (_position < _value.Length && !IsPathSeparator(_value[_position]) && !IsPathCommand(_value[_position])) { _position++; ConsumeCheckpoints(); }
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
                _workCounter.Consume(_cancellationToken);
                _nextCheckpoint += 4096;
            }
        }
    }

    private ref struct DelimitedTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly ParserWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private int _position;
        private int _nextCheckpoint;

        internal DelimitedTokenReader(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _value = value;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal bool TryReadValue(out ReadOnlySpan<char> value)
        {
            while (_position < _value.Length && IsPointSeparator(_value[_position])) { ConsumeCheckpoints(); _position++; }
            ConsumeCheckpoints();
            if (_position == _value.Length) { value = default; return false; }
            int start = _position;
            while (_position < _value.Length && !IsPointSeparator(_value[_position])) { _position++; ConsumeCheckpoints(); }
            value = _value[start.._position];
            return true;
        }

        private void ConsumeCheckpoints()
        {
            while (_nextCheckpoint < _value.Length && _position >= _nextCheckpoint)
            {
                _workCounter.Consume(_cancellationToken);
                _nextCheckpoint += 4096;
            }
        }
    }
}
