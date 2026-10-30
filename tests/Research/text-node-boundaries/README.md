# Text-node fragment metrics

The [capture](capture.json) binds each SVG, supplied [Noto Sans font](../supplied-font-labels/README.md)
and independent Windows/Linux image to three healthy, stable Chrome launches.
Cases cover comments, descriptions, CDATA, empty and ordinary spans, kerning,
ligatures, long reader chunks, hidden paint returns, resource replay and fitting.
The [original acceptance map](original-acceptance.json) preserves 76 historical
declarations through 67 canonical cases, including unequal native companions.
[Scope and validation](../release-baselines/text-acceptance.md) distinguish this
accepted cohort from the remaining precision/raster review.
The [path-baseline acceptance](path-baseline-acceptance.json) adds 120 original
declarations and 84 independent diagnostics. [Baseline evidence](../release-baselines/text-baseline-paint.md)
separates fractional layout/source bounds from recorded integer path painting.
Text nodes retain fragment width rounding. Comments preserve shaping; spans
follow native shaping boundaries when screen font scaling changes the compared
fonts. Controls include unit scale, reflections, a quarter turn, relative
positioning, fitted text paths and replay through uses, symbols, patterns and masks.
Relative-position cases also cover offsets inside `fi`/`ffi` clusters, zero list
entries, glyphless nodes and source prefixes before a following glyph. Fitting
controls vary node form, anchor, positive/negative offsets and gradient paint.
Each retains canonical rendering; representative ownership/replay contracts cover
prepared rendering, limits, cancellation and retry separately.
Anchored rectangle cases cover half-grid widths, fractional relative offsets,
backtracking and nonintegral author origins through both root lists and spans.

Nonuniform fitting and fractional viewport cases cover native control-table
scaling and source-grid baseline painting, including symbol replay and gradients.
The [control-table vectors](../windows-font-unit-scaling/README.md) exercise
axis precision and signed rounding independently of whole-image comparisons.

Unpainted text-path glyphs retain their source bounds for gradients. Cases cover
hidden and unfilled characters at each path position, curved and sloped paths,
and use, symbol and pattern replay, including scaled output. Middle characters
that leave the enclosing bounds unchanged provide positive controls.

Horizontal text paths ignore absolute Y coordinates while retaining their
authored chunk and shaping boundaries. Cases vary scalar/list X and Y, negative
Y values, anchors, ligatures, relative offsets and rotation through direct text
and resource replay. Horizontal glyph paint uses the measured scalar path point
before source-grid rounding, including fractional coordinates and scaled output.
