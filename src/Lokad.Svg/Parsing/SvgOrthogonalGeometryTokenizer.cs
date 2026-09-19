using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses bounded absolute orthogonal path and point-list syntax into charged point storage.</summary>
internal static class SvgOrthogonalGeometryTokenizer
{
    private const int PointStorageBytes = 8;

    internal static BudgetedParserList<SvgPoint> ParsePath(string value, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ValidatePathCommandCount(value, maximumGeometryElements, workCounter, cancellationToken);
        PathTokenReader tokens = new(value);
        if (tokens.End || tokens.ReadCommand() != 'M') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, PointStorageBytes);
        try
        {
            points.Add(new SvgPoint(tokens.ReadInteger(), tokens.ReadInteger()));
            bool closed = false;
            int commandIndex = 0;
            while (!tokens.End)
            {
                if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                char command = tokens.ReadCommand();
                SvgPoint current = points[^1];
                if (command == 'H') points.Add(new SvgPoint(tokens.ReadInteger(), current.Y));
                else if (command == 'V') points.Add(new SvgPoint(current.X, tokens.ReadInteger()));
                else if (command == 'L') points.Add(new SvgPoint(tokens.ReadInteger(), tokens.ReadInteger()));
                else if (command is 'Z' or 'z') { closed = tokens.End; break; }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            }
            if (!closed || points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            if (points.Count > maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                SvgPoint start = points[pointIndex]; SvgPoint end = points[(pointIndex + 1) % points.Count];
                if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            }
            return points;
        }
        catch
        {
            points.Dispose();
            throw;
        }
    }

    internal static BudgetedParserList<SvgPoint> ParsePointList(string value, string elementName, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ValidatePointListVertexCount(value, maximumGeometryElements, workCounter, cancellationToken);
        BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, PointStorageBytes);
        try
        {
            DelimitedTokenReader values = new(value);
            int vertexCount = 0;
            while (values.TryReadValue(out ReadOnlySpan<char> x))
            {
                if ((vertexCount & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!values.TryReadValue(out ReadOnlySpan<char> y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
                if (vertexCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
                points.Add(new SvgPoint(SvgGeometrySyntax.ParseInteger(x), SvgGeometrySyntax.ParseInteger(y)));
                vertexCount++;
            }
            if (points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
            for (int index = 0; index < points.Count; index++)
            {
                SvgPoint start = points[index]; SvgPoint end = points[(index + 1) % points.Count];
                if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
            }
            return points;
        }
        catch
        {
            points.Dispose();
            throw;
        }
    }

    private static void ValidatePathCommandCount(string value, int maximumGeometryElements, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int commandCount = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
            if (value[index] is not ('M' or 'L' or 'H' or 'V' or 'Z' or 'z')) continue;
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

    private static bool IsPathCommand(char value) => value is 'M' or 'L' or 'H' or 'V' or 'Z' or 'z';

    private static bool IsPathSeparator(char value) => value == ',' || char.IsWhiteSpace(value);

    private static bool IsPointSeparator(char value) => value is ',' or ' ' or '\t' or '\r' or '\n';

    private ref struct PathTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal PathTokenReader(ReadOnlySpan<char> value) => _value = value;

        internal bool End
        {
            get
            {
                SkipSeparators();
                return _position == _value.Length;
            }
        }

        internal char ReadCommand()
        {
            SkipSeparators();
            if (_position == _value.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return _value[_position++];
        }

        internal ReadOnlySpan<char> ReadValue()
        {
            SkipSeparators();
            int start = _position;
            while (_position < _value.Length && !IsPathSeparator(_value[_position]) && !IsPathCommand(_value[_position])) _position++;
            if (start == _position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return _value[start.._position];
        }

        internal int ReadInteger()
        {
            ReadOnlySpan<char> value = ReadValue();
            if (!IsPathInteger(value)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return SvgGeometrySyntax.ParseInteger(value);
        }

        private void SkipSeparators()
        {
            while (_position < _value.Length && IsPathSeparator(_value[_position])) _position++;
        }
    }

    private ref struct DelimitedTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal DelimitedTokenReader(ReadOnlySpan<char> value) => _value = value;

        internal bool TryReadValue(out ReadOnlySpan<char> value)
        {
            while (_position < _value.Length && IsPointSeparator(_value[_position])) _position++;
            if (_position == _value.Length) { value = default; return false; }
            int start = _position;
            while (_position < _value.Length && !IsPointSeparator(_value[_position])) _position++;
            value = _value[start.._position];
            return true;
        }
    }
}
