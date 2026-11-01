# Nested filter placement

Six original CC0 controls retain three agreeing healthy, profile-cleaned browser
captures per host in the [manifest](capture.json). Identical platform references
share storage. No existing reference or tolerance changed.

The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgNestedFilterPlacementReferenceTests.cs)
cover a translated control and an affine solid-colour source with nested blur and
group/instance opacity. The latter differed at 1,671 pixels before the placement
fix and is exact on both hosts. Layer sizing now uses the same placed child filter
as replay. A translated child preserves the ancestor's known linear map instead
of introducing an inverse-composition residue.

[Six direct mechanisms](../../Lokad.Svg.Tests/SvgFilterSourceMapTests.cs) protect the
recorded map, translated-child coefficients and four one-ULP changes that must
retain general composition. They do not snap small rotations or shears. Existing
filter/opacity ownership tests cover cancellation, refusal, cleanup and retry;
the controls use one conversion path each.

Four gradient controls remain open: affine linearRGB, the same graph under a
large-coordinate ancestor, sRGB, and a source without its own opacity. They still
differ at 18, 61, 26 and 19 pixels respectively on each host, with identical alpha.
Their sources and independent references remain here for the colour-rounding
investigation; they are not admitted as exact regressions. The
[evidence record](../svgweb/nested-filter-placement.json) retains the original
scene counter, qualification and closure limits.
