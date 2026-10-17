# Static TrueType compatibility

Two declared public programs extend the existing Noto controls: unmodified
[Liberation Sans Regular 2.1.5](https://github.com/liberationfonts/liberation-fonts/releases/tag/2.1.5)
and [DejaVu Sans 2.37](https://github.com/dejavu-fonts/dejavu-fonts/releases/tag/version_2_37).
Their original LICENSE and AUTHORS files are retained beside each font.

Each unchanged 640×144 document uses `Àé AV fi` at 24/48 pixels and fractional
origins. The [declaration](declaration.json) predates renderer comparisons.
[FontTools inventory](font-tables.json) supplies independent cmap/hmtx/composite
data; [HarfBuzz metrics](metrics.json) supply default Latin glyph selection,
kerning, ligature and advance expectations in design units. Tests verify explicit
catalog/provider acquisition and selection, independent metrics and exact
Windows/Linux pixels through representative direct and prepared output paths.

The [manifest](manifest.json) retains unchanged references from three healthy,
stable fresh Chrome 153.0.8010.52 processes per platform and document. Each SVG
embeds its exact caller-supplied Example face; network resources are blocked.
References were frozen before screening and have never been regenerated from
Lokad.Svg output. The regular/bold/real-italic/fallback baseline remains covered by
[public font qualification](../public-font-qualification/README.md).

These controls protect shared Latin feature selection, bounded hint instructions,
simple/composite metric initialization, ClearType compatibility and signed
projection/movement precision. They do not expand font containers or shaping
scope. The original SVG label controls are dedicated to the public domain under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/); the font programs
retain their upstream licenses.
