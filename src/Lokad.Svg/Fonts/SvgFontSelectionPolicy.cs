namespace Lokad.Svg.Fonts;

using System.Collections.ObjectModel;
using Lokad.Svg.Parsing;

/// <summary>Defines immutable host generic mappings and ordered fallback families without font discovery or I/O.</summary>
public sealed class SvgFontSelectionPolicy
{
    /// <summary>Copies host mappings and fallbacks. Names refer to catalog families; no platform defaults or synthetic faces are added.</summary>
    public SvgFontSelectionPolicy(IReadOnlyDictionary<string, string> genericFamilies, IReadOnlyList<string> fallbackFamilies)
    {
        ArgumentNullException.ThrowIfNull(genericFamilies);
        ArgumentNullException.ThrowIfNull(fallbackFamilies);
        if (genericFamilies.Count > SvgFontFamilySyntax.MaximumFamilies || fallbackFamilies.Count > SvgFontFamilySyntax.MaximumFamilies)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The host font selection policy exceeds the supported family count.");
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in genericFamilies)
        {
            ValidateName(pair.Key);
            ValidateName(pair.Value);
            if (!SvgFontFamilySyntax.IsGeneric(pair.Key)) throw new ArgumentException("Generic mappings require a recognized CSS generic keyword.", nameof(genericFamilies));
            if (!mappings.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Generic mappings contain duplicate keywords.", nameof(genericFamilies));
        }
        var fallbacks = new List<string>();
        foreach (string family in fallbackFamilies)
        {
            ValidateName(family);
            if (!fallbacks.Contains(family, StringComparer.OrdinalIgnoreCase)) fallbacks.Add(family);
        }
        GenericFamilies = new ReadOnlyDictionary<string, string>(mappings);
        FallbackFamilies = fallbacks.AsReadOnly();

        static void ValidateName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (name.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "A host font family name exceeds the supported bound.");
        }
    }

    /// <summary>Gets copied case-insensitive generic mappings. An unmapped generic has no authored candidate.</summary>
    public IReadOnlyDictionary<string, string> GenericFamilies { get; }
    /// <summary>Gets copied named fallbacks, considered only after all authored candidates.</summary>
    public IReadOnlyList<string> FallbackFamilies { get; }
}
