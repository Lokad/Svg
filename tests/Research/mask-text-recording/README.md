# Text construction in mask pictures

Mask text records its font size, metrics and fragment positions in the resource
context. The consumer's replay transform places the resulting glyphs. An affine
consumer must not become part of font construction; the text child's own transform
still does. Geometric mask children keep their complete construction map.

The seven [canonical controls](capture.json) distinguish identity, axis-aligned
consumer scale, affine consumer placement, the same affine map on the text child,
object-box normalization, stroked glyphs and text paths. Each uses one ordinary
conversion and independent Windows/Linux references from three fresh healthy, cleaned browser processes.
Affine fill, stroke and path text previously differed in 178/168, 447/327 and
36/113 pixels (Windows/Linux); the four placement controls were exact. Reference
bytes and exact RGBA comparison stay unchanged.

The scoped map borrows the existing preparation state, adds no owner or replay
pipeline, and restores on every exit. Existing text layout, fitting and final
glyph placement remain shared. A separate contract test checks following text
and complete release of the borrowed memory allowance. Existing font/mask
refusal, cancellation and prepared-document tests retain those responsibilities.

The [clip experiments](clip-research.json) are research, **not admitted text-clip
support or additional routine rendering tests**. Twelve authored SVG pairs and
their native PNGs distinguish paint purpose, source versus consumer maps, units,
opacity, positioned spans, glyph stretching, geometry siblings and intersections.
The library's ordinary black paint matches every native paint control. Native
clip alpha nevertheless differs under some consumer maps and object-box units;
nonzero text opacity also scales the clip. Ordinary alpha-mask and clip coverage
can differ at a few pixels even when the mask itself is exact. Do not replace one
operation with another based on equal final images in simpler contexts.

Text clips require an ordered mask route in the pinned browser's
[clip strategy](https://raw.githubusercontent.com/chromium/chromium/153.0.8010.52/third_party/blink/renderer/core/layout/svg/layout_svg_resource_clipper.cc).
The corresponding [mask recording](https://raw.githubusercontent.com/chromium/chromium/153.0.8010.52/third_party/blink/renderer/core/layout/svg/layout_svg_resource_masker.cc)
is a separate operation. The observed opacity controls take precedence over the
clip recorder's stale opacity comment. Remaining clip selection, replay and
composition criteria are explicit in [qualification](qualification.json).

Authored SVGs are CC0-1.0. The supplied Noto program is reused from
[supplied-font-labels](../supplied-font-labels/fonts/OFL.txt), with its hash pinned
in the manifests. No hosted SVG bodies are retained. Routine tests need neither
a browser nor Python; [capture procedures](../../../tools/README.md#capture-and-reference-rules)
use the committed inputs, identical embedded font and pinned native executables.
