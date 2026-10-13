using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Collects a bounded, ordered filter graph during ordinary streaming traversal.</summary>
/// <remarks>The builder owns staging arrays until Finish transfers primitives and merge inputs. Unsupported dormant
/// definitions are retained as admission failures, raised only when a live binding references them.</remarks>
internal sealed class SvgFilterDefinitionBuilder : IDisposable
{
    private readonly record struct ResultName(string? Value);
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly BudgetedSceneList<SvgFilterPrimitive> _primitives;
    private readonly BudgetedSceneList<SvgFilterInput> _mergeInputs;
    private readonly BudgetedParserList<ResultName> _results;
    private readonly BudgetedParserStringStore _strings;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private SvgFilterAdmission? _rejection;
    private SvgFilterRegion _region = new(new(-10, true), new(-10, true), new(120, true), new(120, true), SvgFilterUnits.ObjectBoundingBox);
    private SvgFilterUnits _primitiveUnits;
    private SvgFilterColorSpace _colorSpace;
    private int _mergeDepth = -1;
    private int _mergeIndex = -1;
    internal string? Id { get; private set; }

    private SvgFilterDefinitionBuilder(RenderBudget budget, ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation owner)
    {
        _owner = owner;
        _work = work;
        _token = token;
        _primitives = new(budget, work, token);
        _mergeInputs = new(budget, work, token);
        _results = new(budget, SvgMemoryCategory.Parser, work, token);
        _strings = new(budget);
    }

    internal static SvgFilterDefinitionBuilder Create(XmlReader reader, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RenderBudget.BudgetReservation owner = budget.Reserve(384, SvgMemoryCategory.Parser);
        SvgFilterDefinitionBuilder builder;
        try { builder = new(budget, work, token, owner); }
        catch { owner.Dispose(); throw; }
        try
        {
            builder.Id = reader.GetAttribute("id") is string id ? builder._strings.Copy(id) : null;
            try
            {
                builder._region = new(Length(reader.GetAttribute("x"), new(-10, true)), Length(reader.GetAttribute("y"), new(-10, true)),
                    Length(reader.GetAttribute("width"), new(120, true)), Length(reader.GetAttribute("height"), new(120, true)),
                    Units(reader.GetAttribute("filterUnits"), SvgFilterUnits.ObjectBoundingBox));
                builder._primitiveUnits = Units(reader.GetAttribute("primitiveUnits"), SvgFilterUnits.UserSpace);
                builder._colorSpace = reader.GetAttribute("color-interpolation-filters") switch
                {
                    null or "linearRGB" => SvgFilterColorSpace.LinearRgb,
                    "sRGB" => SvgFilterColorSpace.Srgb,
                    _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter color space is not implemented.")
                };
                builder.ValidateAttributes(reader, "id class x y width height filterUnits primitiveUnits color-interpolation-filters filterRes");
                if (reader.GetAttribute("filterRes") is string legacyResolution)
                {
                    if (legacyResolution.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Legacy filter resolution exceeds the syntax limit.");
                    work.ConsumeText(legacyResolution.Length, token);
                    int count = 0;
                    foreach (Range part in legacyResolution.AsSpan().SplitAny(" ,\t\r\n".AsSpan()))
                    {
                        if (legacyResolution.AsSpan()[part].IsEmpty) continue;
                        count++;
                        if (count > 2 || !int.TryParse(legacyResolution.AsSpan()[part], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) || value < 0)
                            throw new SvgException(SvgErrorCode.MalformedInput, "Legacy filter resolution requires one or two nonnegative integers.");
                    }
                    if (count == 0) throw new SvgException(SvgErrorCode.MalformedInput, "Legacy filter resolution cannot be empty.");
                }
            }
            catch (SvgException error) when (error.ErrorCode != SvgErrorCode.LimitExceeded) { builder.Reject(error); }
            return builder;
        }
        catch { builder.Dispose(); throw; }
    }

    internal void Add(XmlReader reader, bool directChild)
    {
        _work.Consume(_token);
        if (!directChild && _mergeIndex >= 0 && reader.Depth == _mergeDepth + 1
            && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "feMergeNode")
        {
            if (_mergeInputs.Count >= 256) throw new SvgException(SvgErrorCode.LimitExceeded, "A filter may contain at most 256 merge inputs.");
            try
            {
                ValidateAttributes(reader, "id in");
                _mergeInputs.Add(Input(reader.GetAttribute("in"), _mergeIndex));
                SvgFilterPrimitive merge = _primitives[_mergeIndex];
                _primitives[_mergeIndex] = merge with { MergeCount = merge.MergeCount + 1 };
            }
            catch (SvgException error) when (error.ErrorCode != SvgErrorCode.LimitExceeded) { Reject(error); }
            return;
        }
        if (directChild) { _mergeDepth = -1; _mergeIndex = -1; }
        if (_primitives.Count >= 256) throw new SvgException(SvgErrorCode.LimitExceeded, "A filter may contain at most 256 primitives.");
        SvgFilterPrimitive primitive = default;
        try
        {
            if (!directChild || reader.NamespaceURI != "http://www.w3.org/2000/svg")
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested filter content is not implemented.");
            SvgFilterPrimitiveKind kind = reader.LocalName switch
            {
                "feGaussianBlur" => SvgFilterPrimitiveKind.GaussianBlur,
                "feOffset" => SvgFilterPrimitiveKind.Offset,
                "feDropShadow" => SvgFilterPrimitiveKind.DropShadow,
                "feMerge" => SvgFilterPrimitiveKind.Merge,
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter primitive '" + reader.LocalName + "' is not implemented.")
            };
            ValidateAttributes(reader, kind switch
            {
                SvgFilterPrimitiveKind.GaussianBlur => "id in result stdDeviation color-interpolation-filters x y width height",
                SvgFilterPrimitiveKind.Offset => "id in result dx dy color-interpolation-filters x y width height",
                SvgFilterPrimitiveKind.Merge => "id result color-interpolation-filters x y width height",
                _ => "id in result dx dy stdDeviation flood-color flood-opacity color-interpolation-filters x y width height"
            });
            if (reader.GetAttribute("color-interpolation-filters") is string space && space != (_colorSpace == SvgFilterColorSpace.Srgb ? "sRGB" : "linearRGB"))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Mixed primitive filter color spaces are not implemented.");
            double sigmaX = kind == SvgFilterPrimitiveKind.DropShadow ? 2 : 0, sigmaY = sigmaX;
            if (reader.GetAttribute("stdDeviation") is string deviation)
            {
                ReadOnlySpan<char> values = deviation.AsSpan().Trim();
                int separator = values.IndexOfAny(" ,\t\r\n".AsSpan());
                sigmaX = Number(separator < 0 ? values : values[..separator]);
                sigmaY = separator < 0 ? sigmaX : Number(values[(separator + 1)..].TrimStart(" ,\t\r\n".AsSpan()));
                if (sigmaX < 0 || sigmaY < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Blur deviations must be nonnegative.");
            }
            double dx = reader.GetAttribute("dx") is string x ? Number(x.AsSpan()) : kind == SvgFilterPrimitiveKind.DropShadow ? 2 : 0;
            double dy = reader.GetAttribute("dy") is string y ? Number(y.AsSpan()) : kind == SvgFilterPrimitiveKind.DropShadow ? 2 : 0;
            SvgColor flood = reader.GetAttribute("flood-color") is string color ? SvgColorParser.Parse(color, SvgRootParser.CssNamedColors) : new(0, 0, 0, 255);
            float opacity = SvgColorParser.ParsePaintOpacity(reader.GetAttribute("flood-opacity")).Value;
            primitive = new(kind, Input(reader.GetAttribute("in"), _primitives.Count), sigmaX, sigmaY, dx, dy, flood, opacity)
            {
                Region = ReadRegion(reader), MergeFirst = _mergeInputs.Count
            };
            if (kind == SvgFilterPrimitiveKind.Merge && !reader.IsEmptyElement)
            { _mergeDepth = reader.Depth; _mergeIndex = _primitives.Count; }
        }
        catch (SvgException error) when (error.ErrorCode != SvgErrorCode.LimitExceeded) { Reject(error); }
        _primitives.Add(primitive);
        _results.Add(new(reader.GetAttribute("result") is string result ? _strings.Copy(result) : null));

        SvgFilterPrimitiveRegion? ReadRegion(XmlReader input)
        {
            string? x = input.GetAttribute("x"), y = input.GetAttribute("y"), width = input.GetAttribute("width"), height = input.GetAttribute("height");
            if (x is null && y is null && width is null && height is null) return null;
            return new(Read(x), Read(y), Read(width), Read(height));

            SvgFilterLength? Read(string? value)
            {
                if (value is null) return null;
                _work.ConsumeText(value.Length, _token);
                return Length(value, default);
            }
        }

    }

    private SvgFilterInput Input(string? name, int before)
    {
        if (name is null) return before == 0 ? SvgFilterInput.SourceGraphic : new(before - 1);
        if (name == "SourceGraphic") return SvgFilterInput.SourceGraphic;
        if (name == "SourceAlpha") return SvgFilterInput.SourceAlpha;
        for (int index = before - 1; index >= 0; index--)
        {
            _work.Consume(_token);
            if (_results[index].Value == name) return new(index);
        }
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter inputs must name SourceGraphic, SourceAlpha or an earlier result.");
    }

    internal SvgFilterResource Finish()
    {
        OwnedSceneArray<SvgFilterPrimitive> primitives = _primitives.Detach();
        try { return new(_region, _primitiveUnits, _colorSpace, primitives, _rejection) { MergeInputs = _mergeInputs.Detach() }; }
        catch { primitives.Dispose(); throw; }
    }

    private void Reject(SvgException error) => _rejection ??= new(error.ErrorCode, error.Message);

    private void ValidateAttributes(XmlReader reader, string allowed)
    {
        while (reader.MoveToNextAttribute())
        {
            _work.Consume(_token);
            if (reader.NamespaceURI.Length != 0 && reader.NamespaceURI != "http://www.w3.org/1999/xlink") continue;
            bool admitted = false;
            foreach (Range range in allowed.AsSpan().Split(' '))
                if (allowed.AsSpan()[range].SequenceEqual(reader.LocalName.AsSpan())) { admitted = true; break; }
            if (!admitted)
            {
                string name = reader.Name;
                reader.MoveToElement();
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter attribute '" + name + "' is not implemented.");
            }
        }
        reader.MoveToElement();
    }

    private static SvgFilterUnits Units(string? value, SvgFilterUnits fallback) => value switch
    {
        null => fallback, "objectBoundingBox" => SvgFilterUnits.ObjectBoundingBox, "userSpaceOnUse" => SvgFilterUnits.UserSpace,
        _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter coordinate units are not implemented.")
    };

    private static SvgFilterLength Length(string? value, SvgFilterLength fallback)
    {
        if (value is null) return fallback;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        bool percent = token.EndsWith("%", StringComparison.Ordinal);
        if (percent) token = token[..^1];
        else if (token.EndsWith("px", StringComparison.Ordinal)) token = token[..^2];
        return new(Number(token), percent);
    }

    private static double Number(ReadOnlySpan<char> token)
    {
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter number exceeds the syntax limit.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            throw new SvgException(SvgErrorCode.MalformedInput, "Filter values must be finite numbers.");
        return number;
    }

    public void Dispose() { _mergeInputs.Dispose(); _primitives.Dispose(); _results.Dispose(); _strings.Dispose(); _owner.Dispose(); }
}
