# Glyph coverage fallback

The original [AéA document](mixed-coverage.svg) uses two original static TrueType
programs. Primary covers A; Secondary covers A and é, with visibly different A
outlines and advances. Correct selection is Primary/Secondary/Primary, rather
than replacement of the whole string. The declaration records independent glyph
indices, hmtx advances and units per em before candidate comparison.

The document includes ordinary text, textLength spacing, positioned/rotated
glyphs and a curved text path. Both programs have valid full maxp profiles and
no instructions; their outline origins differ from their horizontal metric
origins. These inputs also protect native metric rebasing and Windows simple
dropout selection across both scan passes.

[Frozen references](manifest.json) use Chrome 153.0.8010.52 on Windows and Linux.
Each platform has three healthy stable fresh-process captures; changing each
supplied program independently changes decoded pixels. References and authored
inputs remain unchanged by renderer corrections. Comparison requires exact
decoded RGBA8, ignoring RGB only where both alphas are zero.

Offline controls verify selection, independent advances, direct/prepared output,
lazy acquisition, authored-family precedence over host fallback, shared programs,
source/integrity failures, cancellation/retry and normalized local text instances.
Synthetic controls separately protect supplementary scalars, face-boundary
kerning/ligatures and bounded coverage discovery. No private pack or browser
process is required to run them.

The SVG and both fonts are original CC0 1.0 assets; see [LICENSE](LICENSE.txt).
