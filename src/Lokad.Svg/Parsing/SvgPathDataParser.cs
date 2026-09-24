using System.Globalization;

using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses general path-data line commands into a device-space contour buffer.</summary>
/// <remarks>
/// <para>Supports absolute and relative M, L, H, V and Z with repeated parameters, implicit lineto pairs
/// after moveto, compact signed decimals and exponents, and sign-abutted numbers. Coordinates are finite
/// doubles mapped through the caller transform at parse time; overflowing relative offsets or mapped
/// results fail with <see cref="SvgErrorCode.LimitExceeded"/>.</para>
/// <para>Error taxonomy: grammar and shape violations (missing moveto, truncated parameters, stray values,
/// unexpected letters, non-numeric coordinates) fail as <see cref="SvgErrorCode.MalformedInput"/>.
/// Well-formed curve and arc command letters fail as <see cref="SvgErrorCode.UnsupportedFeature"/>; they
/// name an unadmitted family, not malformed input. A null or whitespace-only value draws nothing with
/// zero contours, matching the SVG rule that a missing path draws nothing.</para>
/// <para>Open trailing contours seal implicitly for fill; a repeated closepath without an open contour is
/// a no-op. Command letters are bounded by the caller limit, points by the builder capacity, and scanning
/// work by the shared counter checkpoints.</para>
/// </remarks>
internal static class SvgPathDataParser
{
    /// <summary>Parses line path data, mapping each vertex and appending sealed contours to the builder.</summary>
    internal static void ParseLinePath(string? value, SvgAffineTransform transform, SvgContourBuilder builder, int maximumCommands, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (maximumCommands < 1) throw new ArgumentOutOfRangeException(nameof(maximumCommands));
        if (value is null) return;
        ValidateCommandBudget(value, maximumCommands, workCounter, cancellationToken);
        PathDataReader reader = new(value, workCounter, cancellationToken);
        if (reader.End) return;
        char command = reader.ReadCommand();
        if (command is not ('M' or 'm')) throw new SvgException(SvgErrorCode.MalformedInput, "Path data must start with a moveto command.");
        double currentX = 0;
        double currentY = 0;
        bool open = false;
        int commandCount = 0;
        while (true)
        {
            if ((commandCount++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            switch (command)
            {
                case 'M':
                case 'm':
                {
                    bool absolute = command == 'M';
                    (double x, double y) = reader.ReadPoint();
                    if (open) builder.EndContour();
                    currentX = absolute ? x : RequireFinite(currentX + x);
                    currentY = absolute ? y : RequireFinite(currentY + y);
                    builder.BeginContour(transform.Apply(currentX, currentY));
                    open = true;
                    while (reader.PeekNumberStart())
                    {
                        (double lineX, double lineY) = reader.ReadPoint();
                        currentX = absolute ? lineX : RequireFinite(currentX + lineX);
                        currentY = absolute ? lineY : RequireFinite(currentY + lineY);
                        builder.AddLine(transform.Apply(currentX, currentY));
                    }

                    break;
                }

                case 'L':
                case 'l':
                case 'H':
                case 'h':
                case 'V':
                case 'v':
                {
                    if (!open) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data line commands need a current point from a moveto command.");
                    bool absolute = char.IsUpper(command);
                    do
                    {
                        if (command is 'L' or 'l')
                        {
                            (double x, double y) = reader.ReadPoint();
                            currentX = absolute ? x : RequireFinite(currentX + x);
                            currentY = absolute ? y : RequireFinite(currentY + y);
                        }
                        else if (command is 'H' or 'h')
                        {
                            double x = reader.ReadNumber();
                            currentX = absolute ? x : RequireFinite(currentX + x);
                        }
                        else
                        {
                            double y = reader.ReadNumber();
                            currentY = absolute ? y : RequireFinite(currentY + y);
                        }

                        builder.AddLine(transform.Apply(currentX, currentY));
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                case 'Z':
                case 'z':
                    if (open)
                    {
                        builder.EndContour();
                        open = false;
                    }

                    break;
                case 'C':
                case 'c':
                case 'S':
                case 's':
                case 'Q':
                case 'q':
                case 'T':
                case 't':
                case 'A':
                case 'a':
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only M/L/H/V/Z line path commands are implemented.");
                default:
                    throw new SvgException(SvgErrorCode.MalformedInput, "Path data contains an unexpected command.");
            }

            if (reader.End) break;
            if (reader.PeekNumberStart()) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data values after a closepath need a command.");
            command = reader.ReadCommand();
        }

        if (open) builder.EndContour();
    }

    private static void ValidateCommandBudget(string value, int maximumCommands, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int commandCount = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
            if (value[index] is not ('M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z')) continue;
            if (commandCount >= maximumCommands) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
            commandCount++;
        }
    }

    private static double RequireFinite(double value)
    {
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Path-data coordinates exceed the admitted numeric range.");
        return value;
    }

    private ref struct PathDataReader
    {
        private readonly ReadOnlySpan<char> _value;
        private readonly ParserWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private int _position;
        private int _nextCheckpoint;

        internal PathDataReader(ReadOnlySpan<char> value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
            if (_position == _value.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Path data ends inside a command.");
            return _value[_position++];
        }

        internal double ReadNumber()
        {
            SkipSeparators();
            ConsumeCheckpoints();
            int start = _position;
            if (_position < _value.Length && _value[_position] is '+' or '-') _position++;
            bool digits = false;
            while (_position < _value.Length && char.IsAsciiDigit(_value[_position])) { _position++; digits = true; }
            if (_position < _value.Length && _value[_position] == '.')
            {
                _position++;
                while (_position < _value.Length && char.IsAsciiDigit(_value[_position])) { _position++; digits = true; }
            }

            if (!digits) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data coordinates must be finite numbers.");
            if (_position < _value.Length && _value[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < _value.Length && _value[_position] is '+' or '-') _position++;
                bool exponentDigits = false;
                while (_position < _value.Length && char.IsAsciiDigit(_value[_position])) { _position++; exponentDigits = true; }
                if (!exponentDigits) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data coordinates must be finite numbers.");
            }

            ConsumeCheckpoints();
            ReadOnlySpan<char> token = _value[start.._position];
            if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Path number values exceed the configured syntax limit.");
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data coordinates must be finite numbers.");
            return result;
        }

        internal (double X, double Y) ReadPoint()
        {
            double x = ReadNumber();
            double y = ReadNumber();
            return (x, y);
        }

        internal bool PeekNumberStart()
        {
            int position = _position;
            while (position < _value.Length && IsSeparator(_value[position])) position++;
            if (position == _value.Length) return false;
            char next = _value[position];
            return char.IsAsciiDigit(next) || next is '-' or '+' or '.';
        }

        private void SkipSeparators()
        {
            while (_position < _value.Length && IsSeparator(_value[_position])) { ConsumeCheckpoints(); _position++; }
        }

        private void ConsumeCheckpoints()
        {
            while (_nextCheckpoint < _value.Length && _position >= _nextCheckpoint)
            {
                _workCounter.Consume(_cancellationToken);
                _nextCheckpoint += 4096;
            }
        }

        private static bool IsSeparator(char value) => value == ',' || char.IsWhiteSpace(value);
    }
}