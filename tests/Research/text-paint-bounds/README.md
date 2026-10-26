# Text paint bounds and shared layout

The numeric captures retain native source-coordinate boxes independently of
rendered images. [Shared-path vectors](shared-path-vectors.json) contain 48
authored inputs with exact Windows/Linux whole-text boxes. Their
[capture record](shared-path-capture.json) records font hashes and three healthy,
stable launches per platform.

[Shared-layout images](shared-layout-images.json) retain 142 inputs, each with
its own unchanged browser reference. Selection protects continuous source
cursors and shaping, one-axis absolute resets, ligature components without
glyphs, empty and multiple paths, closed-path offsets, shared object-box paints,
and specific instance, symbol, pattern, mask and isolation interactions. Filter
controls separate font object boxes from painted capture bounds through ordinary
text, strokes, pattern replay and outer offset/use placement.
Each selection states its purpose. Equal images do not merge distinct sources;
PNG bytes are deduplicated only as storage.

These image cases use canonical owned synchronous conversion. The API matrix
uses one shared-layout fixture across its destination forms. Separate contracts
cover source-local filter remapping, clip/mask wrappers, neighboring pending
uses, borrowed glyph-view ownership, refusal, cancellation during glyph transformation and
retry. Numerical boundaries are not crossed with the entire API matrix.

Run the affected box mechanisms, contracts and image cases while editing; use
the full platform and package gates at a coherent milestone. Older captures in
this folder cover direct/path boxes, spacing, strikes and drawing enclosures.
Shared shader bounds do not replace each fragment's local drawing enclosure.
