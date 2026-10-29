namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Owns active paint scopes and transfers material paint intervals with the normalized layout; all calls share the construction cancellation context.</summary>
/// <remarks>Parser-thread confined. Caller text and contexts remain borrowed; disposal releases every retained scope, interval and layout owner.</remarks>
internal sealed class SvgTextPaintOutput : IDisposable, ISvgTextOutput
{
    private readonly SvgTextLayoutBuffer _layout;
    private readonly BudgetedParserList<(SvgTextPaintStyle Paint, SvgInheritedTextStyle Font, int Identity, int Start, int FirstText)> _scopes;
    private readonly RenderBudget _budget;
    private readonly CancellationToken _cancellationToken;
    private readonly SvgTextGlyphPaint _defaultPaint;
    private BudgetedParserList<SvgTextPaintSpan>? _spans;
    private BudgetedParserList<SvgTextFontSpan>? _fontSpans;
    private readonly SvgInheritedTextStyle _defaultFont;
    private int _length;
    private int _nextScopeIdentity;
    private int _lastEmittedScopeIdentity;
    private bool _startsTextNode;
    private BudgetedParserList<SvgTextNodeBoundary>? _nodeBoundaries;
    private BudgetedParserList<SvgTextRecordingScope>? _recordingScopes;

    internal SvgTextPaintOutput(RenderBudget budget, double letterSpacing, SvgTextPaintStyle paint, CancellationToken cancellationToken)
        : this(budget, letterSpacing, paint, SvgInheritedTextStyle.Default, cancellationToken) { }

    internal SvgTextPaintOutput(RenderBudget budget, double letterSpacing, SvgTextPaintStyle paint, SvgInheritedTextStyle font, CancellationToken cancellationToken)
    {
        _budget = budget;
        _cancellationToken = cancellationToken;
        _defaultPaint = paint.GlyphPaint;
        _defaultFont = font;
        _layout = new(budget, letterSpacing, font.WordSpacing, cancellationToken);
        _scopes = new(budget, SvgMemoryCategory.Parser, cancellationToken);
        try { _scopes.Add((paint, font, 0, 0, -1)); }
        catch { _scopes.Dispose(); _layout.Dispose(); throw; }
    }

    internal SvgTextPaintStyle GetPaint(int scopeDepth) => _scopes[scopeDepth].Paint;
    internal SvgInheritedTextStyle GetFont(int scopeDepth) => _scopes[scopeDepth].Font;
    internal void EnterStyleSpan(SvgTextPaintStyle paint, SvgInheritedTextStyle font) => _scopes.Add((paint, font, checked(++_nextScopeIdentity), _length, -1));
    internal void ExitPaintSpan()
    {
        RetainRecordingScope(_scopes.Count - 1, false);
        _scopes.RemoveLast();
    }

    private void RetainRecordingScope(int index, bool isRoot)
    {
        var scope = _scopes[index];
        if (scope.FirstText < 0) return;
        double stroke = SvgTextRecordingStroke.FromStyle(scope.Paint).Resolve();
        _recordingScopes ??= new(_budget, SvgMemoryCategory.Parser, _cancellationToken);
        _recordingScopes.Add(new(scope.Start, _length, scope.FirstText, stroke, isRoot));
    }

    /// <summary>Marks a source XML text/CDATA node once, independently of reader chunks and normalized appends.</summary>
    internal void BeginTextNode() => _startsTextNode = true;

    public void Append(ReadOnlySpan<char> value, double letterSpacing, int scopeDepth, int maximumLength, CancellationToken cancellationToken)
    {
        if (cancellationToken != _cancellationToken) throw new InvalidOperationException("Text paint output calls must retain their construction cancellation token.");
        cancellationToken.ThrowIfCancellationRequested();
        if (value.IsEmpty) return;
        SvgTextPaintStyle style = GetPaint(scopeDepth);
        if (!style.IsDisplayed) return;
        style.ValidateEmission();
        SvgInheritedTextStyle font = GetFont(scopeDepth);
        _layout.Append(value, letterSpacing, font.WordSpacing, scopeDepth, maximumLength, cancellationToken);
        SvgTextGlyphPaint paint = style.GlyphPaint;
        int start = _length;
        if (paint.IsPainted && _scopes[scopeDepth].FirstText < 0)
        {
            var scope = _scopes[scopeDepth];
            scope.FirstText = start;
            _scopes[scopeDepth] = scope;
        }
        if (_startsTextNode)
        {
            if (start != 0)
            {
                _nodeBoundaries ??= new(_budget, SvgMemoryCategory.Parser, _cancellationToken);
                if (_nodeBoundaries.Count == 0 || _nodeBoundaries[^1].Offset != start) _nodeBoundaries.Add(new(start, _scopes[scopeDepth].Identity != _lastEmittedScopeIdentity));
            }
            _startsTextNode = false;
        }
        _lastEmittedScopeIdentity = _scopes[scopeDepth].Identity;
        _length = checked(start + value.Length);
        if (_fontSpans is not null || !SameFont(font, _defaultFont))
        {
            if (_fontSpans is null)
            {
                _fontSpans = new(_budget, SvgMemoryCategory.Parser, _cancellationToken);
                if (start != 0) _fontSpans.Add(new(0, start, _defaultFont));
            }
            if (_fontSpans.Count != 0 && SameFont(_fontSpans[^1].Font, font)) _fontSpans[^1] = _fontSpans[^1] with { End = _length };
            else _fontSpans.Add(new(start, _length, font));
        }
        if (_spans is null && paint == _defaultPaint) return;
        if (_spans is null)
        {
            _spans = new(_budget, SvgMemoryCategory.Parser, _cancellationToken);
            if (start != 0) _spans.Add(new(0, start, _defaultPaint));
        }
        if (_spans.Count != 0 && _spans[^1].Paint == paint)
            _spans[^1] = _spans[^1] with { End = _length };
        else _spans.Add(new(start, _length, paint));

        static bool SameFont(SvgInheritedTextStyle left, SvgInheritedTextStyle right)
            => string.Equals(left.Family, right.Family, StringComparison.OrdinalIgnoreCase) && left.Weight == right.Weight && left.Italic == right.Italic && left.FontSize == right.FontSize;
    }

    public void EnterPositioningSpan(SvgTextPositionOffset offset, int scopeDepth, CancellationToken cancellationToken)
        => _layout.EnterPositioningSpan(offset, scopeDepth, cancellationToken);
    public void EnterPositioningSpan(SvgTextPositionLists lists, int scopeDepth, CancellationToken cancellationToken)
        => _layout.EnterPositioningSpan(lists, scopeDepth, cancellationToken);
    public void ExitPositioningSpan(int scopeDepth) => _layout.ExitPositioningSpan(scopeDepth);

    public void AppendCollapsedWhitespace(double letterSpacing, int scopeDepth, int maximumLength, CancellationToken cancellationToken)
    {
        if (!_layout.AcceptsCollapsedWhitespace || !GetPaint(scopeDepth).IsDisplayed) return;
        Append(" ", letterSpacing, scopeDepth, maximumLength, cancellationToken);
        _layout.MarkCollapsedWhitespace();
    }

    internal void BeginTextChunk(bool hasPriorText) => _layout.BeginTextChunk(hasPriorText);

    internal SvgExtractedTextRun Detach(SvgTextPathReference? path, bool preserveTrailingWhitespace)
    {
        if (_scopes.Count != 1) throw new InvalidOperationException("Text paint scopes must be closed before layout transfer.");
        RetainRecordingScope(0, true);
        BudgetedTextLayout layout = _layout.Detach(preserveTrailingWhitespace);
        if (_spans is {} paints && paints.Count != 0)
        {
            if (paints[^1].Start == layout.Value.Length) paints.RemoveLast();
            else paints[^1] = paints[^1] with { End = layout.Value.Length };
        }
        if (_fontSpans is {} faces && faces.Count != 0)
        {
            if (faces[^1].Start == layout.Value.Length) faces.RemoveLast();
            else faces[^1] = faces[^1] with { End = layout.Value.Length };
        }
        if (_nodeBoundaries is {} nodes)
            while (nodes.Count != 0 && nodes[^1].Offset >= layout.Value.Length) nodes.RemoveLast();
        OwnedParserArray<SvgTextRecordingScope> recordingScopes = new([], null);
        OwnedParserArray<SvgTextNodeBoundary> boundaries = new([], null);
        OwnedParserArray<SvgTextPaintSpan> spans = new([], null);
        OwnedParserArray<SvgTextFontSpan> fonts = new([], null);
        try
        {
            if (_recordingScopes is {} recordings)
            {
                int retained = 0;
                for (int index = 0; index < recordings.Count; index++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    SvgTextRecordingScope scope = recordings[index];
                    if (scope.FirstText < layout.Value.Length)
                        recordings[retained++] = scope with { End = Math.Min(scope.End, layout.Value.Length) };
                }
                while (recordings.Count > retained) recordings.RemoveLast();
            }
            spans = _spans is null ? new([], null) : _spans.Detach();
            fonts = _fontSpans is null ? new([], null) : _fontSpans.Detach();
            boundaries = _nodeBoundaries is null ? new([], null) : _nodeBoundaries.Detach();
            recordingScopes = _recordingScopes is null ? new([], null) : _recordingScopes.Detach();
            _recordingScopes = null;
            var root = _scopes[0]; root.FirstText = -1; _scopes[0] = root;
            _spans = null;
            _fontSpans = null;
            _nodeBoundaries = null;
            _startsTextNode = false;
            _length = 0;
            return new(layout, path, spans, fonts, boundaries) { RecordingScopes = recordingScopes, ElementStroke = SvgTextRecordingStroke.FromStyle(root.Paint) };
        }
        catch { recordingScopes.Dispose(); boundaries.Dispose(); fonts.Dispose(); spans.Dispose(); layout.Dispose(); throw; }

    }

    public void Dispose()
    {
        _spans?.Dispose();
        _spans = null;
        _fontSpans?.Dispose(); _fontSpans = null;
        _nodeBoundaries?.Dispose(); _nodeBoundaries = null;
        _recordingScopes?.Dispose(); _recordingScopes = null;
        _scopes.Dispose();
        _layout.Dispose();
    }
}
