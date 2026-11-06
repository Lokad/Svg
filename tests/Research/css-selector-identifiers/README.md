# CSS selector admission

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

Three comma-list diagnostics retain blue paint in Chrome: leading, trailing or
repeated commas invalidate the whole list. The library rejects these lists as
`MalformedInput` instead of applying their remaining selectors. The valid commented
list retains red paint. Each new source has its own fresh captures; existing
references are unchanged. [Direct grammar checks](../../Lokad.Svg.Tests/SvgCssSelectorTests.cs)
cover missing/blank entries, comments and inactive members; two contracts cover
early/late stylesheet rejection, cleanup, empty output and retry. Empty and
comment-only stylesheets remain inert.
[Selector-list qualification](../release-baselines/css-selector-lists.json) keeps
the failing-before proof, fresh capture bindings and current validation scope.

[Qualification](../release-baselines/css-selector-identifiers.json) records the
failing-before cases, immutable source/native identities and milestone checks.
