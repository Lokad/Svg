using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Collects a bounded, ordered filter graph during ordinary streaming traversal.</summary>
/// <remarks>The builder owns staging arrays until Finish transfers primitives. Unsupported dormant
/// definitions are retained as admission failures, raised only when a live binding references them.</remarks>
internal sealed class SvgFilterDefinitionBuilder : IDisposable
{
    private readonly record struct ResultName(string? Value);
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly BudgetedSceneList<SvgFilterPrimitive> _primitives;
    private readonly BudgetedParserList<ResultName> _results;
    private readonly BudgetedParserStringStore _strings;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private SvgFilterAdmission? _rejection;
    private SvgFilterRegion _region = new(new(-10, true), new(-10, true), new(120, true), new(120, true), SvgFilterUnits.ObjectBoundingBox);
    private SvgFilterUnits _primitiveUnits;
    private SvgFilterColorSpace _colorSpace;
    internal string? Id { get; private set; }

    private SvgFilterDefinitionBuilder(RenderBudget budget, ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation owner)
    {
        _owner = owner;
        _work = work;
        _token = token;
        _primitives = new(budget, work, token);
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
                builder.ValidateAttributes(reader, "id class x y width height filterUnits primitiveUnits color-interpolation-filters");
            }
            catch (SvgException error) when (error.ErrorCode != SvgErrorCode.LimitExceeded) { builder.Reject(error); }
            return builder;
        }
        catch { builder.Dispose(); throw; }
    }

    internal void Add(XmlReader reader, bool directChild)
    {
        _work.Consume(_token);
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
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter primitive '" + reader.LocalName + "' is not implemented.")
            };
            ValidateAttributes(reader, kind switch
            {
                SvgFilterPrimitiveKind.GaussianBlur => "id in result stdDeviation color-interpolation-filters",
                SvgFilterPrimitiveKind.Offset => "id in result dx dy color-interpolation-filters",
                _ => "id in result dx dy stdDeviation flood-color flood-opacity color-interpolation-filters"
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
            primitive = new(kind, Input(reader.GetAttribute("in")), sigmaX, sigmaY, dx, dy, flood, opacity);
        }
        catch (SvgException error) when (error.ErrorCode != SvgErrorCode.LimitExceeded) { Reject(error); }
        _primitives.Add(primitive);
        _results.Add(new(reader.GetAttribute("result") is string result ? _strings.Copy(result) : null));

        SvgFilterInput Input(string? name)
        {
            if (name is null) return _primitives.Count == 0 ? SvgFilterInput.SourceGraphic : new(_primitives.Count - 1);
            if (name == "SourceGraphic") return SvgFilterInput.SourceGraphic;
            if (name == "SourceAlpha") return SvgFilterInput.SourceAlpha;
            for (int index = _results.Count - 1; index >= 0; index--)
            {
                _work.Consume(_token);
                if (_results[index].Value == name) return new(index);
            }
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter inputs must name SourceGraphic, SourceAlpha or an earlier result.");
        }
    }

    internal SvgFilterResource Finish() => new(_region, _primitiveUnits, _colorSpace, _primitives.Detach(), _rejection);

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

    public void Dispose() { _primitives.Dispose(); _results.Dispose(); _strings.Dispose(); _owner.Dispose(); }
}
