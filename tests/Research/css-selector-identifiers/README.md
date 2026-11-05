# CSS selector identifiers

These original controls are dedicated to CC0-1.0. Each source has three independent
healthy Chrome captures on Windows and Linux, retained in [capture.json](capture.json).
Identical PNG bytes are stored once; every source and capture identity remains.

Selector tokens follow [CSS identifier-start grammar](https://www.w3.org/TR/css-syntax-3/#would-start-an-identifier).
Digit-leading, hyphen-digit and lone-hyphen names are invalid. The five corresponding
diagnostics previously selected red paint where Chrome retained blue. The library
rejects these selectors explicitly under its bounded CSS contract; general CSS
error recovery remains unsupported. Their native images remain unchanged evidence.

[Direct mechanisms](../../Lokad.Svg.Tests/SvgCssSelectorIdentifierTests.cs) protect
identifier starts independently of quoted attribute strings. Representative
contracts retain active, inactive and selector-list admission, parser cleanup,
input immutability, retry and empty stream output on rejection.
[Canonical rendering](../../Lokad.Svg.VisualTests/SvgCssSelectorIdentifierReferenceTests.cs)
retains quoted numeric values, double-hyphen names and a numeric SVG ID through a
local use. SVG ID/class values keep their existing admission rules.

[Qualification](../release-baselines/css-selector-identifiers.json) records the
failing-before cases, immutable source/native identities and milestone checks.
