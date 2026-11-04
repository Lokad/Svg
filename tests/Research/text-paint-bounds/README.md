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

[Precision residuals](precision-residuals.json) retain 39 original paint/fitting
inputs with independent images and exact font/capture bindings. Its observations
preserve the original 31 Windows and 34 Linux differences. The
[pattern-matrix acceptance record](precision-pattern-images.json) closes 13
complete original pairs with 26 canonical rendering cases. These reuse the same
source and image files. The subsequent [shared paint-stage record](paint-stage-images.json)
closes two original radial pairs and adds three orthogonal group/use/symbol
pairs through 10 individual image cases. The [current review](../release-baselines/precision-current.md)
closes all 39 original inputs. The [source-rectangle bank](precision-source-images.json)
protects radial/pattern bounds, output density and opacity through five individual
canonical conversions. Direct numerical mechanisms own rectangle rounding;
ownership contracts remain separate. Three fresh, healthy captures per host reproduce every
original promoted PNG byte. Both font styles, unequal companions, meaningful equal
scope controls and platform differences remain. The
[shared pattern correction](../release-baselines/text-pattern-matrices.md) and
[drawing-stage model](../release-baselines/paint-stages.md) describe the scope;
broader text review retains its original independent targets.

[Filter-source images](filter-source-images.json) retain 135 independently captured
inputs through one canonical conversion path. They cover direct/path text,
stroke overrides, reflection, affine placement, clips, instances, nested filters,
patterns, hidden and paintless text, unavailable stroke servers and alpha.
[Seventeen numeric vectors](filter-source-vectors.json) separate each recorded
owner's enclosing visual box from the SVG element box propagated through the
filter. Native SVG-group mapping independently checks mapped numeric bounds;
[capture provenance](filter-source-capture.json) retains the launch and font evidence.
All references are exact, with separate Windows/Linux images where needed.

Recording owners include their descendants, even when child paints differ.
Hidden or paintless text can contribute to layout without recording a draw.
These image origins remain separate from tight ink capture and shader coordinates.
Dedicated contracts cover copied scope storage, borrowed range views, scratch
refusal/work/cancellation cleanup and a prepared asynchronous stream retry.
The image collection does not repeat the destination/ownership matrix.

The [full precision screen](precision-screening.json) retains all 480 original
candidate/control pairs as separately discovered `Category=Research` cases.
[Generated sources](../../Lokad.Svg.VisualTests/SvgTextPaintBoundsReferenceTests.Screening.cs)
reproduce every original byte hash without adding 960 SVG files. Complete canonical
RGBA digests come from independent native images; dimensions are checked separately
and RGB under zero alpha follows the existing comparer contract. Original PNG hashes,
font/context bindings, platform differences and qualified math companions remain.
Three fresh healthy captures per host agree with every qualified target.

The 39 routine regressions keep their existing sources, PNGs and test identities.
The broader screen adds one canonical conversion per input, without another API or
ownership matrix. Select it with `FullyQualifiedName~Full_text_paint_screen`;
`Category!=Research` keeps the routine development selection unchanged. Shared
metadata, immutable catalogs and font bytes avoid repeated setup; snapshots remain
independent per case. [Qualification](../release-baselines/text-screen-qualification.json)
records source/context joins, retained identities and validation costs.
