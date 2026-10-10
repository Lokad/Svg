# Supplied-font SVG Translate document

The next complete deliverable is the unchanged **SVG Translate textpath** by
Glrx, released under CC0 on [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:SVG_Translate_textpath.svg).
Its 704 upstream bytes, including BOM and CRLF, are retained in
`svg-translate-textpath.svg`. All four text runs, positioned spans and the painted
local path remain original. The [manifest](manifest.json) declares **512×256
transparent**, with an explicit viewport for its 600×300 viewBox. Source, options,
font contract and seven focused controls were declared before capture or screening.

The original `monospace` alias and bold request resolve to public static Noto Sans
Mono Bold face 0. [Font provenance](font-provenance.json) pins its upstream revision
and unchanged evaluation bytes; [OFL](fonts/OFL.txt) permits retaining the program.
Product packages do not bundle it. The browser input embeds that exact face inside
the SVG image under the private family `LokadSuppliedMonospace`, with an explicit
root family mapping. Unquoted CSS `monospace` selects a generic host family; the
private browser mapping implements the declared caller-supplied alias contract
while leaving product SVG bytes unchanged.

Both platforms retain healthy, cleaned, stable reference triples for the original
and all seven controls (48 positive captures). Matching-platform
[missing, corrupt and substituted-face triples](font-use-proof/qualification.json)
retain 18 negative captures. Missing/corrupt embeddings change 9,374 Windows or
9,205 Linux pixels; substitution changes 9,941 Windows or 9,465 Linux pixels.
These witnesses prove actual use of the intended embedded program independently
of candidate rendering. The frozen platform references differ and remain separate.

The document remains **pending (0/1)**. Shared normalized positioning now admits
scalar absolute `tspan x/y`, starts separate anchored text chunks, preserves nested
per-axis precedence and relative offsets, and continues current positions into
later text. Shared bounded structural validation admits element-free rendered and
retained path bodies; nested elements still invalidate rendering. These corrections
remove the original first two rejections. Shared bounded point assembly now admits
the mirrored closing parenthesis, `ROUND_XY_TO_GRID` placement and parent program.
TrueType fits the child before the parent; Linux light fitting uses the assembled
outline. [Eighteen independent native contours](diagnostics/composite-contours.json)
protect both stages. Font header bit 3 now controls integer internal hinting sizes,
while linear advances retain authored sizes. The whole document renders through
all 12 APIs: **Linux matches exactly; Windows retains 62 changed pixels**
(maximum channel error 11). Shared Latin light topology combines near-point vectors and
retains wrapped contour segments before fitting and interpolation. All 44 measured
document outlines match independent native geometry; [ten focused contours](diagnostics/latin-light-near-contours.json)
protect the previously differing combinations. Shared device advance metrics now
preserve authored origins and accumulate regular glyph positions on the SVG
1/64-pixel grid. Text paths accumulate individually rounded character widths.
Independent native font widths, cumulative positions and fractional span origins
have focused controls; the Linux whole document now has exact assertions through
all 12 APIs. Follow every Windows mismatch until it matches unchanged references
through six raw and six independently prepared APIs on both platforms.

Windows symmetric LCD coverage now places quarter strikes on the six-column
sampling grid, preserves half F26 units at implied quadratic midpoints, and gives
quadratic and line crossings the same lower endpoint ownership. These corrections
reduce the whole document from 1,670 differing pixels / maximum error 65 to
237 / 31. [Four independent native RGB masks and two measured midpoint pairs](diagnostics/windows-glyph-coverage.json)
protect them. The remaining edge/coverage discrepancy stays open against the
unchanged references; fitted coordinates agree with all 342 native F26 measurements
in the selected diagnostic cases. Executed scan-control policy now enables smart
dropout coverage, including stubs, on both sampling axes. This preserves thin
strokes between sample centers, corrects 27 additional document pixels and makes
five further positioning controls exact. A complete original native A16 texture
and [two original CC0 scan-control comparisons](diagnostics/windows-scan-control.json)
protect the correction. Native textures and bounds are stable through three fresh
processes for each case. Directed exact center ownership and stable quadratic
evaluation now share crossings per sample row/column, correcting another 148
document pixels. Comparing both directed limits at exact vertices preserves
corners without duplicate transitions. [Seven complete original CC0 textures](diagnostics/windows-contour-boundaries.json)
and two complete supplied-face masks in the glyph controls protect the change;
all nine agree through three fresh native processes. Other scan-control
extensions and the remaining curve differences stay separate work.

[The curve-cell diagnostics](diagnostics/windows-curve-cells.json) retain 30
translated native RGB textures per case and exactly inverted binary cell maps.
The S32 mask differs from exact analytic coverage in only three cells; cropping
to the bottom curves preserves them. Two original CC0 quadratic pairs isolate
scan conversion independently: every authored integer F26 coordinate agrees
with 48 native GC observations, but exact subdivision changes the native mask.
All retained textures and bounds agree through three fresh native processes,
with enabled and disabled grid fitting. The two split forms are already exact
and have full-texture regression protection across all 30 translations each;
the two unsplit forms remain pending. These are diagnostic controls, separate
from the seven SVG controls and whole-document completion. The renderer stays
at 62 Windows document differences and four integer-strike differences.

The seven controls are separate from document completion. All render through the
12 APIs; five independent direct-text equivalents protect actual position and
path semantics. API and layout success do not count as exact native pixels.
Linux has **7/7 controls exact**, protected by frozen-reference assertions.
Windows has **6/7 controls exact**, protected by frozen-reference assertions:

| Control | Purpose | Windows changed pixels / maximum channel error |
| --- | --- | --- |
| Scalar x reset | Reset x while retaining y. | 0 / 0 |
| Scalar y reset | Reset y while retaining x. | 0 / 0 |
| Nested absolute/relative | Combine parent coordinates and descendant offsets. | 0 / 0 |
| Continued current position | Keep reset y and advance x into the next span. | 0 / 0 |
| Empty path body | Retain an explicitly closed local baseline. | 0 / 0 |
| Integer strikes | Two sizes and curved glyphs from the document. | 4 / 12 |
| Scaled strikes | Preserve the original root map and fractional strikes. | 0 / 0 |

Complete Windows/Linux qualification passes 6,678 tests per platform and preserves
all 27/27 corporate documents / 258/258 variants, 2,849 prior PNGs and 2,755 prior
exact comparisons. Keep wider finite
G9 list/rotate/font/paint/baseline/curve promises and the older live-text defects
visible without making exhaustive combinations prerequisites for this document.
Broader online sourcing resumes after the identified remaining support promises.
