namespace Lokad.Svg.Fonts;

using System.Globalization;
using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

internal readonly record struct SvgFontCoverageRequest(SvgFontFaceRequest Request, uint[] Scalars);

/// <summary>Collects bounded normalized scalar needs without opening fonts or retaining text.</summary>
/// <remarks>Parser-local owner of aggregate dictionary storage. Shared normalization emits into this
/// output; positioning belongs to later layout and is not accepted here. Materialization releases
/// collection storage and retains the scalar copies under one aggregate reservation through font
/// acquisition. Callers finish all borrowers before disposal; use is confined to one operation.
/// Every scalar and retained-entry scan observes the supplied work/token context.</remarks>
internal sealed class SvgFontCoverageDiscovery : ISvgTextOutput, IDisposable
{
    private readonly record struct FontKey(string Family, int Weight, bool Italic)
    {
        internal bool Matches(SvgFontFaceRequest request)
            => Weight == request.Weight && Italic == request.Italic && Family.Equals(request.Family, StringComparison.OrdinalIgnoreCase);
        public bool Equals(FontKey other) => Weight == other.Weight && Italic == other.Italic && Family.Equals(other.Family, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Family), Weight, Italic);
    }
    private readonly record struct Entry(FontKey Font, uint Scalar);
    private readonly BudgetedParserDictionary<Entry, Entry> _entries;
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private SvgTextWhitespaceNormalizer _normalizer;
    private FontKey _font = new(string.Empty, 400, false);
    private FontKey? _separator;
    private bool _hasOutput;
    private char _highSurrogate;
    private int _count;
    private RenderBudget.BudgetReservation? _resultsReservation;
    private SvgFontCoverageRequest[]? _requests;
    private bool _disposed;

    internal SvgFontCoverageDiscovery(RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        _budget = budget; _work = work; _token = token;
        _entries = new(budget, SvgMemoryCategory.Parser, 128);
    }

    internal void BeginText()
    {
        _normalizer = new(SvgTextWhitespaceMode.Default);
        _separator = null; _hasOutput = false; _highSurrogate = '\0';
    }

    internal void SetFont(SvgInheritedTextStyle font)
        => _font = new(font.Family ?? "serif", font.Weight, font.Italic);

    internal void Observe(ReadOnlySpan<char> value, SvgTextWhitespaceMode mode)
        => _normalizer.Append(value, mode, this, int.MaxValue, _work, _token);

    internal void ObserveNormalized(SvgFontFaceRequest request, ReadOnlySpan<char> value)
        => AddScalars(new(request.Family, request.Weight, request.Italic), value);

    internal uint[] GetScalars(SvgFontFaceRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_requests is not null) throw new InvalidOperationException("Coverage collection has already finished.");
        using BudgetedParserList<uint> scalars = new(_budget, SvgMemoryCategory.Parser, _work, _token);
        foreach (Entry entry in _entries.Values)
        {
            _work.Consume(_token);
            if (entry.Font.Matches(request)) scalars.Add(entry.Scalar);
        }
        long bytes = checked(32L + sizeof(uint) * (long)scalars.Count);
        if (_resultsReservation is null) _resultsReservation = _budget.Reserve(bytes, SvgMemoryCategory.Parser);
        else _resultsReservation.Grow(bytes);
        return scalars.AsSpan().ToArray();
    }

    /// <summary>Borrows the materialized request/scalar arrays until this owner is disposed; acquisition must finish first.</summary>
    internal SvgFontCoverageRequest[] Requests
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _requests ?? throw new InvalidOperationException("Coverage collection has not finished.");
        }
    }

    /// <summary>Finishes collection, reserving retained copies before allocation and dropping the superseded dictionary.</summary>
    internal void Materialize(SvgFontFaceRequest[] requests)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_requests is not null) throw new InvalidOperationException("Coverage collection has already finished.");
        long bytes = checked(32L + 16L * requests.Length);
        if (_resultsReservation is null) _resultsReservation = _budget.Reserve(bytes, SvgMemoryCategory.Parser);
        else _resultsReservation.Grow(bytes);
        SvgFontCoverageRequest[] result = new SvgFontCoverageRequest[requests.Length];
        for (int index = 0; index < requests.Length; index++)
        {
            _work.Consume(_token);
            result[index] = new(requests[index], GetScalars(requests[index]));
        }
        _requests = result;
        _entries.Dispose();
    }

    /// <summary>Consumes a borrowed normalized span, retaining only distinct font/scalar pairs and a pending separator owner.</summary>
    public void Append(ReadOnlySpan<char> value, double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (value.IsEmpty) return;
        if (_separator is {} separator) { AddScalar(separator, 32); _separator = null; }
        AddScalars(_font, value); _hasOutput = true;
    }

    /// <summary>Defers a separator until subsequent text proves that it survives normalization; no caller storage is retained.</summary>
    public void AppendCollapsedWhitespace(double letterSpacing, int scopeDepth, int maximumLength, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (_hasOutput) _separator ??= _font;
    }

    void ISvgTextOutput.EnterPositioningSpan(SvgTextPositionOffset offset, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Coverage discovery consumes normalized characters, not positioning events.");
    void ISvgTextOutput.EnterPositioningSpan(SvgTextPositionLists lists, int scopeDepth, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Coverage discovery consumes normalized characters, not positioning events.");
    void ISvgTextOutput.ExitPositioningSpan(int scopeDepth)
        => throw new InvalidOperationException("Coverage discovery consumes normalized characters, not positioning events.");

    private void AddScalars(FontKey font, ReadOnlySpan<char> value)
    {
        int first = 0, end = value.Length;
        if (_highSurrogate != '\0')
        {
            if (end == 0 || !char.IsLowSurrogate(value[0])) throw new SvgException(SvgErrorCode.MalformedInput, "Text contains an incomplete Unicode scalar.");
            AddScalar(font, (uint)char.ConvertToUtf32(_highSurrogate, value[0]));
            _highSurrogate = '\0'; first = 1;
        }
        if (first < end && char.IsHighSurrogate(value[end - 1])) _highSurrogate = value[--end];
        foreach (Rune scalar in value[first..end].EnumerateRunes()) AddScalar(font, (uint)scalar.Value);
    }

    private void AddScalar(FontKey font, uint scalar)
    {
        _work.Consume(_token);
        Entry entry = new(font, scalar);
        if (_entries.TryGetValue(entry, out _)) return;
        if (_count == SvgTextLimits.MaximumCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Font coverage needs exceed the admitted text bound.");
        _entries.TryAdd(entry, entry); _count++;
    }

    /// <summary>Retains the admitted Latin/symbol fallback boundary; combining or joining needs never become scalar-by-scalar substitution.</summary>
    internal static bool AllowsScalarFallback(uint scalar)
    {
        Rune rune = new((int)scalar);
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format or UnicodeCategory.Control) return false;
        if (Rune.IsLetter(rune)) return scalar <= 0x024F || scalar is >= 0x1E00 and <= 0x1EFF || scalar is >= 0x1D400 and <= 0x1D7FF;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _requests = null;
        _entries.Dispose();
        _resultsReservation?.Dispose();
        _disposed = true;
    }
}
