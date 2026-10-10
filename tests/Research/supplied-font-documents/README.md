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
all 12 APIs: **2,026 Windows / 474 Linux changed pixels** remain (maximum channel
errors 84 / 69). Shared Latin light topology combines near-point vectors and
retains wrapped contour segments before fitting and interpolation. All 44 measured
document outlines match independent native geometry; [ten focused contours](diagnostics/latin-light-near-contours.json)
protect the previously differing combinations. Follow every mismatch until it matches unchanged references
through six raw and six independently prepared APIs on both platforms.

The seven controls are separate from document completion. All render through the
12 APIs; five independent direct-text equivalents protect actual position and
path semantics. API and layout success do not count as exact native pixels.
Linux has **7/7 controls exact**, protected by frozen-reference assertions.
Windows still has **0/7 controls exact**:

| Control | Purpose | Windows changed pixels / maximum channel error |
| --- | --- | --- |
| Scalar x reset | Reset x while retaining y. | 4 / 6 |
| Scalar y reset | Reset y while retaining x. | 2 / 6 |
| Nested absolute/relative | Combine parent coordinates and descendant offsets. | 2 / 6 |
| Continued current position | Keep reset y and advance x into the next span. | 6 / 6 |
| Empty path body | Retain an explicitly closed local baseline. | 2 / 6 |
| Integer strikes | Two sizes and curved glyphs from the document. | 8 / 12 |
| Scaled strikes | Preserve the original root map and fractional strikes. | 4 / 8 |

Complete Windows/Linux qualification passes 6,623 tests per platform and preserves
all 27/27 corporate documents / 258/258 variants, 2,849 prior PNGs and 2,755 prior
exact comparisons. Keep wider finite
G9 list/rotate/font/paint/baseline/curve promises and the older live-text defects
visible without making exhaustive combinations prerequisites for this document.
Broader online sourcing resumes after the identified remaining support promises.
