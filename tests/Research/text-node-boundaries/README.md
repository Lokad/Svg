# Text-node fragment metrics

The [capture](capture.json) binds each SVG, supplied [Noto Sans font](../supplied-font-labels/README.md)
and independent Windows/Linux image to three healthy, stable Chrome launches.
Cases cover comments, descriptions, CDATA, empty and ordinary spans, kerning,
ligatures, long reader chunks, hidden paint returns, resource replay and fitting.
Text nodes retain fragment width rounding. Comments preserve shaping; spans
follow native shaping boundaries when screen font scaling changes the compared
fonts. Controls include unit scale, reflections, a quarter turn, relative
positioning, fitted text paths and replay through uses, symbols, patterns and masks.
Relative-position cases also cover offsets inside `fi`/`ffi` clusters, zero list
entries, glyphless nodes and source prefixes before a following glyph. Fitting
controls vary node form, anchor, positive/negative offsets and gradient paint;
their images are checked through direct and prepared rendering, limits and retry.
Anchored rectangle cases cover half-grid widths, fractional relative offsets,
backtracking and nonintegral author origins through both root lists and spans.

Two fitting inputs retain an existing three-pixel Windows difference separately
from the exact cases. Their native images are preserved; both inputs are exact
on Linux. No image tolerance or reference is changed to hide that difference.
