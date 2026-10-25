// Adapts SkGradientBaseShader stop preparation. Copyright 2016 Google Inc.;
// 2018, 2022 Google LLC. See THIRD_PARTY_NOTICES.txt for the pinned BSD source.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Borrows validated source stops and exposes native normal-gradient canonicalization without allocating storage.</summary>
/// <remarks>At most 256 authored stops and two implicit endpoints. Creation and enumeration consume caller work and observe
/// cancellation. Source storage must remain immutable and alive until every draw-local enumerator finishes; no owner transfers.</remarks>
internal readonly ref struct SvgGradientStops
{
    private readonly ReadOnlySpan<SvgGradientStop> _source;
    private readonly bool _firstImplicit, _periodic;
    private readonly int _rawCount;
    internal bool Uniform { get; }
    internal int Count { get; }

    private SvgGradientStops(ReadOnlySpan<SvgGradientStop> source, bool firstImplicit, bool lastImplicit, bool periodic, bool uniform, int count)
    {
        _source = source;
        _firstImplicit = firstImplicit;
        _periodic = periodic;
        _rawCount = source.Length + (firstImplicit ? 1 : 0) + (lastImplicit ? 1 : 0);
        Uniform = uniform;
        Count = count;
    }

    internal static SvgGradientStops Create(ReadOnlySpan<SvgGradientStop> source, SvgGradientSpread spread, ParserWorkCounter work, CancellationToken token)
    {
        if (source.Length is < 2 or > 256) throw new InvalidOperationException("Canonical gradient stops require two to 256 validated source entries.");
        bool firstImplicit = (float)source[0].Offset > 0, lastImplicit = (float)source[^1].Offset != 1;
        bool periodic = spread != SvgGradientSpread.Pad;
        SvgGradientStops raw = new(source, firstImplicit, lastImplicit, periodic, false, 0);
        float step = (float)raw.Raw(1).Offset, previous = 0, groupPosition = 0;
        bool uniform = true;
        int groupLength = 1, count = 0;
        for (int index = 1; index <= raw._rawCount; index++)
        {
            work.Consume(token);
            float position = index == raw._rawCount ? float.PositiveInfinity : (float)raw.Raw(index).Offset;
            if (index < raw._rawCount)
            {
                uniform &= MathF.Abs(step - (position - previous)) <= 1f / 4096;
                previous = position;
            }
            if (position == groupPosition) { groupLength++; continue; }
            bool duplicate = groupLength > 1;
            if (!(duplicate && periodic && groupPosition == 0)) count++;
            if (duplicate && !(periodic && groupPosition == 1)) count++;
            groupPosition = position;
            groupLength = 1;
        }
        return new(source, firstImplicit, lastImplicit, periodic, uniform, uniform ? raw._rawCount : count);
    }

    private SvgGradientStop Raw(int index)
    {
        if (index == 0) return _source[0] with { Offset = 0 };
        if (index == _rawCount - 1) return _source[^1] with { Offset = 1 };
        SvgGradientStop stop = _source[index - (_firstImplicit ? 1 : 0)];
        return stop with { Offset = (float)stop.Offset };
    }

    internal Enumerator GetEnumerator(ParserWorkCounter work, CancellationToken token) => new(this, work, token);

    /// <summary>Streams borrowed canonical colors; each instance is confined to its caller's preparation stack.</summary>
    internal ref struct Enumerator
    {
        private readonly SvgGradientStops _stops;
        private readonly ParserWorkCounter _work;
        private readonly CancellationToken _token;
        private int _next, _pending;
        internal SvgGradientStop Current { get; private set; }

        internal Enumerator(SvgGradientStops stops, ParserWorkCounter work, CancellationToken token)
        {
            _stops = stops; _work = work; _token = token;
            _next = 0; _pending = -1; Current = default;
        }

        internal bool MoveNext()
        {
            if (_pending >= 0)
            {
                _work.Consume(_token);
                Current = _stops.Raw(_pending); _pending = -1;
                return true;
            }
            while (_next < _stops._rawCount)
            {
                _work.Consume(_token);
                if (_stops.Uniform)
                {
                    int index = _next++;
                    Current = _stops.Raw(index) with { Offset = (float)index / (_stops._rawCount - 1) };
                    return true;
                }
                int start = _next, end = start + 1;
                float position = (float)_stops.Raw(start).Offset;
                while (end < _stops._rawCount)
                {
                    _work.Consume(_token);
                    if ((float)_stops.Raw(end).Offset != position) break;
                    end++;
                }
                _next = end;
                bool duplicate = end - start > 1;
                bool left = !(duplicate && _stops._periodic && position == 0);
                bool right = duplicate && !(_stops._periodic && position == 1);
                if (left)
                {
                    Current = _stops.Raw(start);
                    if (right) _pending = end - 1;
                    return true;
                }
                if (right) { Current = _stops.Raw(end - 1); return true; }
            }
            return false;
        }
    }
}
