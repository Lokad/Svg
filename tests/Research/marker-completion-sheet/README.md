# Marker completion

One original CC0 sheet and six controls cover em/ex dimensions and anchors,
marker-container/child CSS cascade and directional zero/repeated vertices on
open, closed and wholly zero paths. The original bytes/options stay unchanged.
The baseline rejects the whole sheet and relative-length controls; three other
controls are already exact and the closed-path control differs by 76 pixels.

Exact axis angles preserve the half-turn bisector boundary; native composition
rounds the marker pose through a translated parent. Font-relative coordinates
reuse authored font inheritance and the existing supplied x-height/x-glyph
metrics. Ex factors remain deferred until a live marker needs its face.

The first cold ex captures are invalid oracle attempts: the browser caches a
fallback-font viewport before the supplied face loads, then uses that face for
reference points. A native absolute control reproduces that mixed geometry
exactly. The [study](../../../artifacts/marker-completion-20261016/font-length-oracle-correction.json)
retains those attempts and the independent calibration. Preloading the identical
public font URI under an unrelated outer family before constructing the unchanged
SVG image fixes the race. Cold data-URI and Blob captures agree; preloaded image
geometry agrees with independent loaded-DOM lengths on Windows/Linux. Missing-face
SVG output is unchanged by the unrelated outer preload, proving SVG-local font use.

The bank retains 42 qualified healthy stable output captures and six missing-face
captures. The whole sheet/ex control use corrected preloaded triples; five controls
reuse their original triples. No established reference or original SVG is replaced.
[PLAN](../../../PLAN.md) owns remaining qualification and release decisions.

The whole document and six controls are fully qualified on Windows/Linux:
**8,123 tests/platform**, incremental build plus complete tests **78.8s / 243.4s**.
All 8,105 prior cases remain. E4 stays open for its finite container display/
visibility and inherited stroke-attribute admission follow-up; T3/S17 also remain.
