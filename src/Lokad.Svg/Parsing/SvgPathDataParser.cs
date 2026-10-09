// Axis-aligned path mapping follows Skia SkMatrix.cpp at the revision
// recorded in THIRD_PARTY_NOTICES.txt. Copyright 2006 The Android Open Source Project.
using System.Globalization;

using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses general path data into a device-space contour buffer.</summary>
/// <remarks>
/// <para>Supports absolute and relative M, L, H, V, C, S, Q, T, A and Z with repeated parameters,
/// implicit lineto pairs after moveto, reflected smooth controls, strict single-character arc flags,
/// compact signed decimals and exponents, and sign-abutted numbers. Validated authored numbers and relative/smooth
/// updates use independently verified float lanes. The caller maps coordinates at parse time: axis-aligned
/// device maps use float lanes while general affine computation retains doubles. Curves flatten eagerly with the caller policy. Overflowing
/// relative offsets or mapped results fail with <see cref="SvgErrorCode.LimitExceeded"/>.</para>
/// <para>Error taxonomy: grammar and shape violations (missing moveto, truncated parameters, stray values,
/// unexpected letters, non-numeric coordinates) fail as <see cref="SvgErrorCode.MalformedInput"/>.
/// A null or whitespace-only value draws nothing with zero contours, matching the SVG rule that a
/// missing path draws nothing. Mapped arcs normalize their source-space endpoint operands into
/// bounded rational conics before applying the affine map. Translation-only arcs keep the established
/// endpoint route; retained source replay preserves native float geometry at raster time.</para>
/// <para>Open trailing contours seal implicitly for fill; a repeated closepath without an open contour is
/// a no-op. Command letters are bounded by the caller limit, points by the builder capacity, and scanning
/// work by the shared counter checkpoints.</para>
/// </remarks>
internal static class SvgPathDataParser
{
    /// <summary>Finds the drawable prefix through the last complete segment, using the strict parser's number and flag grammar.</summary>
    /// <remarks>Only malformed path syntax ends the prefix. Full-input command/work limits and cancellation remain failures.
    /// This scan owns no storage; the rendering caller reserves any shortened string before copying it.
    /// Strict contour parsing remains available for callers requiring syntax errors.</remarks>
    internal static (int Length, int CommandCount) ReadRenderablePrefix(string value, int maximumCommands, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (maximumCommands < 1) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
        int count = ValidateCommandBudget(value, maximumCommands, workCounter, cancellationToken);
        PathDataReader reader = new(value, workCounter, cancellationToken);
        int completed = 0;
        try
        {
            if (reader.End) return (value.Length, count);
            char command = reader.ReadCommand();
            if (command is not ('M' or 'm')) return (0, count);
            while (true)
            {
                int parameters = command switch
                {
                    'M' or 'm' or 'L' or 'l' or 'T' or 't' => 2,
                    'H' or 'h' or 'V' or 'v' => 1,
                    'Q' or 'q' or 'S' or 's' => 4,
                    'C' or 'c' => 6,
                    'A' or 'a' => 7,
                    'Z' or 'z' => 0,
                    _ => -1,
                };
                if (parameters < 0) return (completed, count);
                if (parameters == 0) completed = reader.Position;
                else
                {
                    do
                    {
                        for (int index = 0; index < parameters; index++)
                        {
                            if (command is 'A' or 'a' && index is 3 or 4) reader.ReadFlag();
                            else reader.ReadNumber();
                        }
                        completed = reader.Position;
                    } while (reader.PeekNumberStart());
                }
                if (reader.End) return (value.Length, count);
                if (reader.PeekNumberStart()) return (completed, count);
                command = reader.ReadCommand();
            }
        }
        catch (SvgException error) when (error.ErrorCode == SvgErrorCode.MalformedInput) { return (completed, count); }
    }

    /// <summary>Parses path data, mapping each vertex and appending sealed contours to the builder.</summary>
    /// <summary>Reports whether path data holds one moveto subpath sealed by a trailing closepath.</summary>
    /// <remarks>Letters outside digit-or-dot exponents are commands, so single M/m plus single Z/z with a trailing Z reads closed-single without tokenizing; lowering validates the grammar first, so callers only invoke this on successfully lowered paths. Charges scanning work by input length and observes cancellation through the counter.</remarks>
    internal static bool IsSingleClosedPath(string? value, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        if (string.IsNullOrEmpty(value)) return false;
        workCounter.ConsumeBytes(value.Length, cancellationToken);
        int movetos = 0;
        int closes = 0;
        char lastCommand = '\0';
        bool hasCommand = false;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current is 'e' or 'E')
            {
                if (index > 0 && (char.IsDigit(value[index - 1]) || value[index - 1] == '.')) continue;
                return false;
            }
            if ((current < 'A' || current > 'Z') && (current < 'a' || current > 'z')) continue;
            hasCommand = true;
            lastCommand = current;
            if (current is 'M' or 'm') movetos++;
            else if (current is 'Z' or 'z') closes++;
        }
        return hasCommand && movetos == 1 && closes == 1 && lastCommand is 'Z' or 'z';
    }

    internal static void ParsePath(string? value, SvgAffineTransform transform, SvgContourBuilder builder, SvgFlattenPolicy flattenPolicy, int maximumCommands, ParserWorkCounter workCounter, CancellationToken cancellationToken, BudgetedParserList<bool>? closedEnds)
        => ParsePath(value, transform, builder, flattenPolicy, maximumCommands, workCounter, cancellationToken, closedEnds, null);

    internal static void ParsePath(string? value, SvgAffineTransform transform, SvgContourBuilder builder, SvgFlattenPolicy flattenPolicy, int maximumCommands, ParserWorkCounter workCounter, CancellationToken cancellationToken, BudgetedParserList<bool>? closedEnds, BudgetedParserList<SvgAuthoredPathCommand>? authored)
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
        bool hasSubpathStart = false;
        double subpathStartX = 0;
        double subpathStartY = 0;
        double previousCubicX = 0;
        double previousCubicY = 0;
        bool hasPreviousCubic = false;
        double previousQuadraticX = 0;
        double previousQuadraticY = 0;
        bool hasPreviousQuadratic = false;
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
                    if (open) SealContour(false);
                    currentX = absolute ? x : RequireFinite(currentX + x);
                    currentY = absolute ? y : RequireFinite(currentY + y);
                    subpathStartX = currentX;
                    subpathStartY = currentY;
                    hasSubpathStart = true;
                    BeginContour(currentX, currentY);
                    open = true;
                    hasPreviousCubic = false;
                    hasPreviousQuadratic = false;
                    while (reader.PeekNumberStart())
                    {
                        (double lineX, double lineY) = reader.ReadPoint();
                        currentX = absolute ? lineX : RequireFinite(currentX + lineX);
                        currentY = absolute ? lineY : RequireFinite(currentY + lineY);
                        AddLine(currentX, currentY);
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
                    if (!open)
                    {
                        if (!hasSubpathStart) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data line commands need a current point from a moveto command.");
                        BeginContour(currentX, currentY);
                        open = true;
                    }
                    hasPreviousCubic = false;
                    hasPreviousQuadratic = false;
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

                        AddLine(currentX, currentY);
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                case 'Z':
                case 'z':
                    hasPreviousCubic = false;
                    hasPreviousQuadratic = false;
                    if (open)
                    {
                        SealContour(true);
                        open = false;
                        currentX = subpathStartX;
                        currentY = subpathStartY;
                    }

                    break;
                case 'C':
                case 'c':
                {
                    if (!open)
                    {
                        if (!hasSubpathStart) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data curve commands need a current point from a moveto command.");
                        BeginContour(currentX, currentY);
                        open = true;
                    }
                    bool absolute = command == 'C';
                    do
                    {
                        (double firstX, double firstY) = reader.ReadPoint();
                        (double secondX, double secondY) = reader.ReadPoint();
                        (double x, double y) = reader.ReadPoint();
                        double control1X = absolute ? firstX : RequireFinite(currentX + firstX);
                        double control1Y = absolute ? firstY : RequireFinite(currentY + firstY);
                        double control2X = absolute ? secondX : RequireFinite(currentX + secondX);
                        double control2Y = absolute ? secondY : RequireFinite(currentY + secondY);
                        double endX = absolute ? x : RequireFinite(currentX + x);
                        double endY = absolute ? y : RequireFinite(currentY + y);
                        AddCubic(control1X, control1Y, control2X, control2Y, endX, endY);
                        previousCubicX = control2X;
                        previousCubicY = control2Y;
                        hasPreviousCubic = true;
                        hasPreviousQuadratic = false;
                        currentX = endX;
                        currentY = endY;
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                case 'S':
                case 's':
                {
                    if (!open)
                    {
                        if (!hasSubpathStart) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data curve commands need a current point from a moveto command.");
                        BeginContour(currentX, currentY);
                        open = true;
                    }
                    bool absolute = command == 'S';
                    do
                    {
                        double control1X = hasPreviousCubic ? RequireFinite(currentX + RequireFinite(currentX - previousCubicX)) : currentX;
                        double control1Y = hasPreviousCubic ? RequireFinite(currentY + RequireFinite(currentY - previousCubicY)) : currentY;
                        (double secondX, double secondY) = reader.ReadPoint();
                        (double x, double y) = reader.ReadPoint();
                        double control2X = absolute ? secondX : RequireFinite(currentX + secondX);
                        double control2Y = absolute ? secondY : RequireFinite(currentY + secondY);
                        double endX = absolute ? x : RequireFinite(currentX + x);
                        double endY = absolute ? y : RequireFinite(currentY + y);
                        AddCubic(control1X, control1Y, control2X, control2Y, endX, endY);
                        previousCubicX = control2X;
                        previousCubicY = control2Y;
                        hasPreviousCubic = true;
                        hasPreviousQuadratic = false;
                        currentX = endX;
                        currentY = endY;
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                case 'Q':
                case 'q':
                {
                    if (!open)
                    {
                        if (!hasSubpathStart) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data curve commands need a current point from a moveto command.");
                        BeginContour(currentX, currentY);
                        open = true;
                    }
                    bool absolute = command == 'Q';
                    do
                    {
                        (double firstX, double firstY) = reader.ReadPoint();
                        (double x, double y) = reader.ReadPoint();
                        double controlX = absolute ? firstX : RequireFinite(currentX + firstX);
                        double controlY = absolute ? firstY : RequireFinite(currentY + firstY);
                        double endX = absolute ? x : RequireFinite(currentX + x);
                        double endY = absolute ? y : RequireFinite(currentY + y);
                        AddQuadratic(controlX, controlY, endX, endY);
                        previousQuadraticX = controlX;
                        previousQuadraticY = controlY;
                        hasPreviousQuadratic = true;
                        hasPreviousCubic = false;
                        currentX = endX;
                        currentY = endY;
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                case 'T':
                case 't':
                {
                    if (!open)
                    {
                        if (!hasSubpathStart) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data curve commands need a current point from a moveto command.");
                        BeginContour(currentX, currentY);
                        open = true;
                    }
                    bool absolute = command == 'T';
                    do
                    {
                        double controlX = hasPreviousQuadratic ? RequireFinite(currentX + RequireFinite(currentX - previousQuadraticX)) : currentX;
                        double controlY = hasPreviousQuadratic ? RequireFinite(currentY + RequireFinite(currentY - previousQuadraticY)) : currentY;
                        (double x, double y) = reader.ReadPoint();
                        double endX = absolute ? x : RequireFinite(currentX + x);
                        double endY = absolute ? y : RequireFinite(currentY + y);
                        AddQuadratic(controlX, controlY, endX, endY);
                        previousQuadraticX = controlX;
                        previousQuadraticY = controlY;
                        hasPreviousQuadratic = true;
                        hasPreviousCubic = false;
                        currentX = endX;
                        currentY = endY;
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                case 'A':
                case 'a':
                {
                    if (!open)
                    {
                        if (!hasSubpathStart) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data curve commands need a current point from a moveto command.");
                        BeginContour(currentX, currentY);
                        open = true;
                    }
                    bool absolute = command == 'A';
                    do
                    {
                        double radiusX = reader.ReadNumber();
                        double radiusY = reader.ReadNumber();
                        double rotation = reader.ReadNumber();
                        bool largeArc = reader.ReadFlag();
                        bool sweep = reader.ReadFlag();
                        (double x, double y) = reader.ReadPoint();
                        double endX = absolute ? x : RequireFinite(currentX + x);
                        double endY = absolute ? y : RequireFinite(currentY + y);
                        authored?.Add(new(SvgAuthoredPathVerb.Arc, (largeArc ? SvgAuthoredArcFlags.Large : SvgAuthoredArcFlags.None) | (sweep ? SvgAuthoredArcFlags.Sweep : SvgAuthoredArcFlags.None), new(radiusX, radiusY), new(rotation, 0), new(endX, endY)));
                        if (transform.IsAxisAligned && transform.A == 1 && transform.D == 1)
                            builder.AddArc(radiusX, radiusY, rotation, largeArc, sweep, MapPathPoint(transform, endX, endY), flattenPolicy, cancellationToken);
                        else AppendMappedArc(new(currentX, currentY), radiusX, radiusY, rotation, largeArc, sweep, new(endX, endY));
                        hasPreviousCubic = false;
                        hasPreviousQuadratic = false;
                        currentX = endX;
                        currentY = endY;
                    }
                    while (reader.PeekNumberStart());
                    break;
                }

                default:
                    throw new SvgException(SvgErrorCode.MalformedInput, "Path data contains an unexpected command.");
            }

            if (reader.End) break;
            if (reader.PeekNumberStart()) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data values after a closepath need a command.");
            command = reader.ReadCommand();
        }

        if (open) SealContour(false);

        // Authored floats enter the demonstrated axis-aligned device map directly;
        // general affine computation remains on its separately gated double route.
        static SvgDevicePoint MapPathPoint(SvgAffineTransform deviceMap, double x, double y)
        {
            if (!deviceMap.IsAxisAligned) return deviceMap.Apply(x, y);
            float mappedX = (float)deviceMap.A * (float)x + (float)deviceMap.E;
            float mappedY = (float)deviceMap.D * (float)y + (float)deviceMap.F;
            if (!float.IsFinite(mappedX) || !float.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
            return new SvgDevicePoint(mappedX, mappedY);
        }

        void BeginContour(double x, double y)
        {
            authored?.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(x, y), default, default));
            builder.BeginContour(MapPathPoint(transform, x, y));
        }

        void AddLine(double x, double y)
        {
            authored?.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(x, y), default, default));
            builder.AddLine(MapPathPoint(transform, x, y));
        }

        void AddCubic(double firstX, double firstY, double secondX, double secondY, double endX, double endY)
        {
            authored?.Add(new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, new(firstX, firstY), new(secondX, secondY), new(endX, endY)));
            builder.AddCubic(MapPathPoint(transform, firstX, firstY), MapPathPoint(transform, secondX, secondY), MapPathPoint(transform, endX, endY), flattenPolicy, cancellationToken);
        }

        void AddQuadratic(double controlX, double controlY, double endX, double endY)
        {
            authored?.Add(new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(controlX, controlY), new(endX, endY), default));
            builder.AddQuadratic(MapPathPoint(transform, controlX, controlY), MapPathPoint(transform, endX, endY), flattenPolicy, cancellationToken);
        }

        void AppendMappedArc(SvgDevicePoint start, double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, SvgDevicePoint end)
        {
            // Normalize in source space, then map each retained conic. Radii and
            // sweep cannot be transformed independently under a general affine map.
            for (int phase = 0; phase <= SvgEndpointArc.MaximumConicCount; phase++) workCounter.Consume(cancellationToken);
            Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
            int count = SvgEndpointArc.Convert(start, radiusX, radiusY, rotation, largeArc, sweep, end, arcs, cancellationToken);
            if (count == 0) builder.AddLine(MapPathPoint(transform, end.X, end.Y));
            else for (int index = 0; index < count; index++)
                builder.AddConic(MapPathPoint(transform, arcs[index].Control.X, arcs[index].Control.Y), MapPathPoint(transform, arcs[index].End.X, arcs[index].End.Y), arcs[index].Weight, 8, cancellationToken);
        }

        void SealContour(bool explicitlyClosed)
        {
            authored?.Add(new(explicitlyClosed ? SvgAuthoredPathVerb.Close : SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
            builder.EndContour();
            if (closedEnds is not null) closedEnds.Add(explicitlyClosed);
        }
    }

    private static int ValidateCommandBudget(string value, int maximumCommands, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int commandCount = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
            if (value[index] is not ('M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't' or 'A' or 'a' or 'Z' or 'z')) continue;
            if (commandCount >= maximumCommands) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
            commandCount++;
        }
        return commandCount;
    }

    private static double RequireFinite(double value)
    {
        float narrowed = (float)value;
        if (!float.IsFinite(narrowed)) throw new SvgException(SvgErrorCode.LimitExceeded, "Path-data coordinates exceed the admitted numeric range.");
        return narrowed;
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

        internal int Position => _position;

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
            return SvgFloatNumber.ReadValidated(token, _workCounter, _cancellationToken);
        }

        internal (double X, double Y) ReadPoint()
        {
            double x = ReadNumber();
            double y = ReadNumber();
            return (x, y);
        }

        internal bool ReadFlag()
        {
            SkipSeparators();
            ConsumeCheckpoints();
            if (_position == _value.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data arc flags must be 0 or 1.");
            char flag = _value[_position++];
            if (flag is not ('0' or '1')) throw new SvgException(SvgErrorCode.MalformedInput, "Path-data arc flags must be 0 or 1.");
            ConsumeCheckpoints();
            return flag == '1';
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
