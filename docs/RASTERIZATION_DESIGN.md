# Rasterization transition design

The current renderer is intentionally limited to integer-aligned rectangle edges.
It must not extend that loop with sample-count approximations for paths, circles,
rotated rectangles, or fractional edges: Chrome-exact RGBA acceptance remains the
criterion.

The next geometry subsystem will keep parsed commands separate from raster work:

- Normalize supported shapes and paths into compact contour/edge buffers, charging
  their storage before allocation and bounding commands, contours, subdivisions,
  and active edges.
- Transform geometry into device space before scan conversion. Axis-aligned integer
  rectangles may retain the existing fast path only when it produces identical
  results to the general compositor.
- Use a deterministic coverage representation for each pixel and source-over it
  through the existing alpha compositor. Edge coverage, not a blanket pixel
  tolerance, is the unresolved Chrome-calibration problem.
- Check cancellation during parsing, subdivision, edge construction, and bounded
  scanline/tile work. Account temporary edge/coverage buffers separately from the
  destination surface.

Before admitting a non-rectangular feature, add original CC0 probes for integer
and fractional boundaries, transparency overlap, fill rule behavior, transforms,
and degeneracies. Capture each probe repeatedly with the pinned Chrome recipe;
only zero-difference decoded RGBA comparisons become `exact` fixtures. A stable
difference remains classified, not tolerated.

## Circle feasibility finding

The original [6×6 opaque-circle probe](../tests/Research/circle-6x6.svg)
(`cx=3`, `cy=3`, `r=2`) and its pinned-Chrome reference are retained in the
[research inventory](../tests/Research/manifest.json). Three fresh-process
captures under Chrome 153.0.8010.52 / recipe 2 had identical decoded pixels. A
bounded adaptive integration of the mathematical circle/square intersection was
close but not compatible: 12 decoded pixels differed, with a maximum alpha-channel
error of 17. This rejects both a plain analytic-area implementation and
supersampling as an admission strategy; circle support remains explicitly
unsupported pending scan-converter calibration.

## Linear-gradient feasibility finding

For the opaque red-to-blue
[four-pixel `objectBoundingBox` probe (since graduated from the research inventory),
the retained three-capture Chrome reference gave `DF001F`, `A00060`, `5F009F`,
`2000E0`. Ordinary pixel-centre, sRGB interpolation differs by one channel in
every pixel. A 16-pixel repeat had 12 mismatched pixels, each by one channel,
showing a backend-specific fixed-point interpolation rule rather than a rounding
choice that is safely inferable from the short probe. Both observations are now explained by the dithering rule below: the probe pixels match the transposed-dither model on all 12 channel values.
implemented for two-stop objectBoundingBox linear gradients on rectangles under per-fixture oracle gating.

Dither finding (2026-09-22): Chrome applies deterministic ordered dithering to linear gradients. Seven probes pin the model `floor(sRGB_direct_ramp(t) + Bayer8_transposed[x % 8][y % 8] / 64)` with pixel-centre `t` and per-channel independence: the shallow red ramp (64x64, 4096 pixels x R/B), a red-only shallow variant (512 pixels), the graduated four-pixel reference (all 12 channel values), white-black/green/black 64x8 ramps, and horizontal/vertical 64-pixel red-blue ramps. SRGB-direct interpolation is confirmed (gray midpoint reads 125, linear-light would give 186). Combined fit: 64 mismatching values of 19980 (99.68%), every residual exactly one LSB in eight transposed-matrix cells under steep slopes only; gentle slopes match perfectly including all constant channels. The residuals resist global bias, divisor, phase, shear and stop-dither explanations and differ between probes at identical pixels, so no static positional dither rule closes them; a slope-coupled second stage (e.g., LUT quantization) is the leading hypothesis. The calibration probes graduated from the research inventory once the rule was implemented (all recipe 3, three byte-identical captures each; evidence table retained here). S13 admits two-stop objectBoundingBox gradients on rectangles with per-fixture oracle gating under this rule; fixtures touching the eight residual cells stay unsupported until the second stage is understood. Transposed dither values `Dither[x % 8][y % 8]` in 64ths, rows `y % 8`, columns `x % 8`: `0 48 12 60 3 51 15 63` / `32 16 44 28 35 19 47 31` / `8 56 4 52 11 59 7 55` / `40 24 36 20 43 27 39 23` / `2 50 14 62 1 49 13 61` / `34 18 46 30 33 17 45 29` / `10 58 6 54 9 57 5 53` / `42 26 38 22 41 25 37 21`.

## General-path calibration inventory

The research inventory also retains three-capture, byte- and decoded-pixel-stable
Windows-pinned-Chrome references for an
[8×8 diagonal triangle](../tests/Research/diagonal-triangle-8x8.svg),
[fractional-coordinate diagonal triangle](../tests/Research/diagonal-triangle-fractional-8x8.svg),
[half-sixteenth-grid diagonal triangle](../tests/Research/diagonal-triangle-half-grid-8x8.svg),
[transparent semi-opaque diagonal overlap](../tests/Research/diagonal-overlap-8x8.svg),
[half-sixteenth-grid transparent semi-opaque diagonal overlap](../tests/Research/diagonal-overlap-half-grid-8x8.svg),
[disjoint semi-opaque layer-opacity](../tests/Research/diagonal-layer-opacity-16x8.svg),
[half-sixteenth-grid disjoint layer-opacity](../tests/Research/diagonal-layer-opacity-half-grid-16x8.svg),
[cubic arch](../tests/Research/cubic-arch-8x8.svg),
and [round stroke join](../tests/Research/round-join-8x8.svg). They supply the first
integer, grid-aligned fractional, half-grid-coordinate, two-phase semi-opaque overlap,
and pre-compositing opacity coverage, subdivision, and outline
targets for P1.3. The orthogonal evenodd-hole probe graduated with disjoint
orthogonal multi-subpath fill-rule support (exact against its retained oracle
before removal); the remaining probes stay explicitly unsupported. The current converter asserts
`UnsupportedFeature` for each remaining probe; neither their stable browser captures nor this
inventory admits a general path or stroke feature.

The first deliberately simple experiment uses an opaque red, pixel-centre
inside/outside rule for the diagonal triangle. It differs from the retained Chrome
reference in 16 of 64 pixels, with maximum RGBA-channel error 255 and maximum
alpha error 127 (mismatch bounds `(1,1)`–`(6,6)`). This is a quantitative rejection
of binary pixel-centre coverage; it is not an implementation candidate.

A uniform 16×16 supersampling variant improves the maximum channel/alpha error to
7, but remains non-exact in 14 pixels with the same mismatch bounds. The result is
also retained as a negative finding: higher uniform sample counts are not an
admission strategy without a calibrated device-space edge rule.

Exact triangle–pixel intersection area with deterministic nearest alpha rounding
improves again to seven changed pixels and maximum channel/alpha error three, but
is still non-exact at the same bounds. Thus continuous geometric coverage alone
does not identify Chrome's raster rule; edge quantization and/or compositing must
be investigated before a general path feature can be admitted.

The seven alpha deltas versus analytic coverage are mixed (`-2,+1,-1,+1,+3,-2,+3`),
so replacing nearest alpha rounding with one global floor or ceiling rule cannot
explain the reference. This is evidence for a device-space edge-quantization
hypothesis, not proof of any particular Chromium/Skia implementation.

A source-guided fixed-point characterization then converts each analytic pixel
area to a 1/256 alpha scale by truncation. It improves to six changed pixels,
but still has a maximum channel/alpha error of three. Adding endpoint snapping
below 8 or above 247 gives the identical result. The matched Skia source routes
this small convex path through its mask writer, rather than its snapping RLE
writer; that source detail supports treating the two observed results as one
output-quantization finding. Any next candidate must also model the edge/scanline
calculation and still match this probe exactly.

The fractional-origin companion has the same topology but vertices at
`(1.25,1.125)`, `(7.25,2.125)` and `(2.25,7.125)`. On its stable reference, the
same fixed-alpha-area baseline instead changes 22 of 64 pixels, with maximum
channel error 255 and alpha error 32. Thus the near-fit for integral input does
not transfer across device phase; the next candidate must make its input-edge
quantization explicit rather than applying an output-only adjustment.

A separate edge-strip model rounds each edge crossing at a row boundary to the
nearest sixteenth of a pixel, then computes its clipped strip area. It reduces
the integral probe to two one-unit alpha differences, but changes 23 pixels on
the fractional-origin probe (maximum channel/alpha error 255/32). It is therefore
useful evidence that device-space edge snapping matters, but is still a rejected
phase-specific approximation: partial-row handling and alpha accumulation remain
unmodelled.

The half-sixteenth companion places all vertices at a rounding tie. Its
fixed-alpha baseline changes 22 pixels (maximum channel/alpha error 255/23),
while the snapped-strip baseline changes 20 (255/24). Although the latter is a
small improvement over the grid-aligned fractional phase, neither is close to an
admission result. The family therefore rules out choosing a grid phase or
tie-breaking rule based on the integral near-fit.

An explicit input-vertex sixteenth-grid experiment rounds every half-grid vertex
down or up before the fixed-alpha area calculation. Flooring changes 20 pixels
(maximum channel/alpha error 255/29), while ceiling changes 21 (20/20); both
mismatch envelopes are `(1,1)`–`(7,7)`. Vertex snapping alone therefore does not
explain the reference, independently of the earlier edge-crossing experiment.

The corresponding sixteenth-grid edge-crossing experiment rounds each strip
intersection consistently down or up instead of to nearest. On the half-grid
reference, floor changes 19 pixels (maximum channel/alpha error 255/22) and
ceiling changes 22 (255/34), both with envelope `(1,1)`–`(7,7)`. Neither directed
rounding rule explains the 20-pixel nearest-crossing result, so a fixed crossing
tie direction is not a calibration candidate.

A source-guided partial experiment takes only the pinned `SkAnalyticEdge::SnapY`
input rule: round each endpoint Y to the nearest quarter pixel, with ties upward,
then pass the resulting triangle to the existing nearest-sixteenth edge-strip
model. It deliberately does not claim that Chrome selected that walker or model
the walker's fixed edge stepping, per-row alpha accumulation, or final compositing.
The integral probe remains at two changed pixels with maximum channel/alpha error
1/1. The grid-aligned fractional probe improves from 23 changed pixels (255/32)
to 12 (2/2), and the half-grid probe improves from 20 (255/24) to 16 (8/8).
Those improvements identify endpoint-Y quantization as a worthwhile component to
isolate, but the result is non-exact for every phase and remains a rejected
partial model.

Replacing that model's direct crossing calculation with 16.16 fixed-point edge
slopes and stepping before the same sixteenth-X snap preserves its integral and
grid-aligned-fractional results. It further improves the half-grid reference from
16 changed pixels (8/8) to 15 (2/2). This is still not a port of the matched
walker: it retains analytic strip-area quantization rather than the source's
per-row alpha operations, and it makes no claim about the browser raster path.
It is therefore another non-exact component result, not a geometry admission.

Quantizing and saturating each of those strip contributions independently to one
byte has the identical 2/64, 12/64 and 15/64 envelopes. A deeper partial model
then ports the matched source's trapezoid-alpha branches and byte-mask
accumulation while retaining the source-guided endpoint and edge setup. It is
decisively worse: the integral reference changes 6 pixels with maximum
channel/alpha error 255/211, and both fractional references change 18 with
255/249. The model still omits the full source edge builder and does not identify
Chrome's selected backend, so this is a rejection of those alpha operations as an
isolated explanation, not an inference about production Skia behavior. The next
probe must overlap semi-opaque diagonal paths on a transparent background; the
current one-path, opaque family cannot distinguish coverage from source-over.

The original two-path [8×8 diagonal overlap](../tests/Research/diagonal-overlap-8x8.svg)
and its [half-sixteenth-grid phase companion](../tests/Research/diagonal-overlap-half-grid-8x8.svg)
each have three fresh Windows-pinned captures that are byte- and decoded-pixel-identical.
On the original phase, continuous analytic coverage plus straight source-over changes
37 of 64 pixels (maximum channel/alpha error 33/27). Reusing the source-guided
fixed-edge coverage lowers this to 26 (3/1). Applying byte opacity and source-over
reduces the count to 22, but increases the maximum channel error to six while alpha
remains within one. The conventional divide-by-255 product and the tested Skia-style
alpha product produce identical decoded candidates for that probe.

The phase companion rejects treating the latter result as a calibration lead:
source-guided fixed-edge coverage plus continuous source-over changes 29 of 64
pixels (13/5), while byte opacity/compositing changes 23 (13/5). Both mismatch
envelopes are `(0,1)`–`(6,7)`. The original near fit was therefore phase-specific.

A distinct discrete compositing experiment retains `coverage × opacity` as byte
premultiplied channels across both source-over operations, then unpremultiplies only
once for PNG output. This is not an edge-rounding variant. With ordinary rounded
byte products and rounded final unpremultiplication, it improves the original phase
to 19 changed pixels (maximum channel/alpha error 3/1) and the companion to 20
(13/5). Flooring final unpremultiplication is worse at 26 (4/1) and 24 (14/5),
respectively; the tested Skia-style byte product is also worse at 21 (6/2) and 21
(13/5). The retained premultiplied intermediate is therefore a measurable
cross-phase improvement over the straight-byte candidate, but it is non-exact and
the phase companion still has a large channel error. It rejects straight
intermediate compositing as sufficient; it does not identify the browser backend.
Do not tune it or admit geometry. Any resumed P1.3 work needs a new discriminating
mechanism that matches both overlap phases exactly.

The two disjoint 16×8 layer-opacity probes preserve the red and blue triangles'
respective original and half-grid phases, but translate them into separate halves
of a transparent image. They therefore measure each path's coverage × 0.5 opacity
conversion before source-over can contribute. Both have three fresh
Windows-pinned captures that are byte- and decoded-pixel-identical. Starting with
the source-guided fixed-edge coverage, the ordinary rounded byte product misses
18 of 128 pixels in the original phase (maximum channel/alpha error 1/1, bounds
`(1,1)`–`(14,6)`) and 18 in the phase companion (5/5,
`(1,1)`–`(14,7)`). The tested Skia-style byte product has the same two envelopes.
Thus the retained residual begins before red/blue source-over, and compositing
rounding alone cannot repair the prior overlap candidates. This is a classified
negative calibration result: it does not identify Chrome's coverage backend and
does not admit general geometry.

## Coverage-dependent alpha rounding on rectangle-pure overlaps (2026-09-23 decision probes)

Question: does any context-free source-over rounding explain the overlap
known differences (original-logo-translucent-mark 376 px, original-logo-use-fade-mark
256 px, both maximum error 1)? All inputs below are axis-aligned integer rectangles,
so no geometric coverage uncertainty is involved.

Method: retained known-difference references plus fresh two-run decision captures
(identical decoded pixels across both runs): q1 single mid-channel layers
(`#1aF8` and `#ff000080` side by side), q2 direct-rect partial red-over-red overlap,
q3a 64x64 full red-over-blue coverage, q3b 2x2 partial red-over-blue corner overlap.
Decision captures are not retained and carry no manifest entries; the q1/q2/q3 PNGs
remain scratch artifacts. Fresh 64x64-open measurements below come from the
candidate builds described; every stated renderer output was executed, never derived.

Facts (decoded RGBA, reference versus candidate):
- Full-canvas half-over-half stacks composite to alpha 191: the retained
  integer-alpha-overlap 2x2 red-over-blue reference (exact) and q3a 64x64
  red-over-blue (85,0,170,191 on every pixel, both runs). Channels agree at
  (85,0,170) in both sizes.
- Partial half-over-half stacks composite to alpha 192: use-fade-mark 16x16
  use-instance overlap (retained known-difference reference), direct-rect Q2
  16x16 overlap ((255,0,0,192) on all 256 pixels, both runs), and q3b 1x1 corner
  overlap ((85,0,170,192), both runs).
- Canvas size (2 versus 64), reference path (direct rects versus use instances),
  corner versus interior position, and paint colors do not move the split; canvas
  coverage does. The channel bytes are identical across the split; only the alpha
  differs by one, so no context-free formula over the local (128,128) stack can
  produce both 191 and 192.
- Single half layers: q1 gives (17,171,255,136) for `#1aF8` and (255,0,0,128) for
  half red. The pinned Hex_colors_with_alpha_are_supported expectation
  (17,170,...) is therefore oracle-contradicted on green; Comma_form_rgb_and_rgba
  ((4,5,6,128)) and Half_opacity_layers_truncate_retained_alpha ([85,0,170,191])
  pin truncating behavior without oracle backing. Those tests were left unchanged.

Eliminated uniform rules (each implemented and measured against the full core
suite plus the four overlap references; the tree was reverted afterwards, so no
library change below is committed):
- Status-quo truncation misses every partial alpha (use-fade 256, translucent 320
  alpha deltas, q3b, Q2) and q1 green (170 versus 171).
- Rounded output alpha only: repairs partial alphas but breaks the retained
  integer-alpha-overlap (192 versus 191) and shifts (24,24)-class reds 255 to 254
  against their references.
- Byte-premultiplied intermediates with truncating unpremultiply: zeroes use-fade
  and all overlap alphas but breaks integer-alpha-overlap and degrades single
  `#1aF8` red 17 to 16 against q1.
- Byte-premultiplied intermediates with rounded alpha and rounded unpremultiply:
  zeroes use-fade-mark (256 to 0) and translucent-mark (376 to 0), matches q1
  singles exactly, and leaves viewbox-mark-24 (28, geometric) and
  group-fade-mark-white (1792, separate mechanism) byte-identical — but breaks
  integer-alpha-overlap (192 versus retained 191) and the three non-oracle unit
  expectations above. Recipe for reproduction: round each source and destination
  channel product to bytes when premultiplying, round the output alpha, and round
  (not truncate) the final unpremultiply division.

Decision: no context-free source-over rule is admitted. The full-canvas exception
is load-bearing: identical local stacks composite to 191 when they cover the
canvas and 192 otherwise. A coverage-dependent rounding was deliberately not
implemented — it has no backend mechanism and its domain across nested viewports,
viewport scaling, groups, and clipping is ill-defined, so it would be a
fixture-specific branch by another name. The white-background overshoot (group
fade over white, unchanged under every compositing variant tried) needs its own
mechanism. White-background per-region table (same session, retained reference versus current renderer, all alpha 255): red-only pixels render (255,127,127) against (255,126,126); blue-only and blue-over-red overlap pixels render (127,127,255) against (126,126,255). Every non-saturated channel overshoots by exactly one; saturated channels and alpha are exact. No local product was found that yields these numerators: the uniform -1 across all non-saturated channels with exact alpha has no single-term explanation in the blend, group-opacity, background-color, or dither candidates above, so the white overshoot stays an independent mechanism with this table as its retained characterization.
Mixed-stack disambiguation (same session, fresh two-run decision captures with
run-to-run agreement, not retained): a full red-half base with a partial blue-half
16x16 patch composites its interior probe pixel to (85,0,170,192), and a partial
red-half base with a full blue-half cover composites its red-region probe pixel to
(85,0,170,192). Predictions were: bottom-layer coverage 191/192, stack-unanimity
192/192, per-operation source coverage 192/191. Measured 192/192 eliminates the
bottom-layer rule (it required 191 on the full-base case) and the per-operation
source rule (it required 191 on the full-cover case). The sole survivor is
stack-unanimity — truncate the alpha if and only if every layer covering the pixel
is full-canvas, else round — which fits every datum above (integer-overlap,
use-fade, Q2, q3a, q3b, both mixed cases). It is still not implemented, for the
same reason: "full-canvas" has no clean domain once groups isolate surfaces,
clips cut coverage implicitly, and viewBox/output scales fractionalize edges, so
a per-pixel all-full flag would be spooky action at a distance, not a mechanism.
The backend-path question stands escalated.

The remaining
question — which backend path treats full layers differently — is escalated as a
genuine feasibility uncertainty: it cannot be settled from outside the browser.
## Calibration source pin

The Chromium `153.0.8010.52` tag used by the retained Windows recipe pins
[`third_party/skia`](https://skia.googlesource.com/skia/+/f8b66b7597c4cc859d3ed190e9c6872241e6721c)
to `f8b66b7597c4cc859d3ed190e9c6872241e6721c` in its
[`DEPS`](https://chromium.googlesource.com/chromium/src.git/+/refs/tags/153.0.8010.52/DEPS).
This selects the source revision for any backend investigation. It does not
establish which Chromium path rendered SVG, its runtime configuration, or the
cause of a pixel difference; a candidate rule still has to match the retained
probes exactly.

## Path-heavy illustration classification

[Tiger, Telemachos, Openclipart 16551](../tests/Research/tiger-16551.svg) is the
first classified real illustration: its downloaded original hash is preserved in
the research manifest, it contains 49 paths and four groups, and the
SPDX-materialized input has a three-capture stable 362×296 Windows-pinned-Chrome
reference. The current converter returns `UnsupportedFeature`; this classifies a
needed general-path/composition workload without treating an unsupported browser
reference as a feature admission.

## Input vertex-grid snapping elimination (2026-09-23 decision analysis)
Question: does input vertex quantization to a device-space subpixel grid, followed by exact area computation and output rounding, explain the diagonal hold-out family? This was the explicitly open next step after the fixed-point output-quantization finding; uniform supersampling and output-only adjustments were already eliminated and are not revisited here.

Method: six retained transparent probes (base, mirror, winding-reordered, 2x-scaled, fractional at 1/8 increments, half-grid at 1/32 increments). An independent exact-area implementation cross-checks the admitted SvgPolygonCoverage kernel totals (17.5 for the 8x8 family, 70.0 for the 16x16 probe) and reproduces the recorded baselines (base 7 mismatches/maxerr 3 under round, fractional 21/32). Grid search over 30 combinations: snap denominators 1, 2, 4, 8, 16 by nearest/floor/ceil, then exact area with round or trunc output. Analysis scripts are scratch artifacts, not retained.

Facts: the best combination (quarter-grid nearest snap with round output) still leaves 72 mismatches across the six probes with maxerr 12, against 78 for the unsnapped round baseline. Every combination fails decisively; the fractional and half-grid probes dominate with 21+ mismatches each and errors up to 104. The 16x16 top-row alpha identity with the 8x8 top row is corner-anchored self-similarity (both are vertex-A corner pixels with exact coverage 5/6), not rasterizer scale-invariance, so it constrains nothing beyond the base row.

Decision: input vertex-grid snapping is eliminated as a general mechanism for these grids, modes and output rules. The device-space edge/scanline calculation remains the open question. No implementation change; the diagonal lowering gate is unchanged.

## Scanline edge-sampling elimination (2026-09-23 decision analysis)
Question: does evaluating triangle edge crossings once per pixel row and filling the resulting span, with span endpoints or edge slopes optionally quantized, explain the diagonal hold-out? This tests the scanline half of the open edge/scanline question; vertex snapping was eliminated previously.

Method: the same six retained transparent probes. Span variants: crossings sampled at row top, center, or bottom; span endpoints snapped to 1/4, 1/8, 1/16 grids; edge slopes snapped to 1/8, 1/16; output round or trunc. Per-pixel coverage is the column clip of the sampled span. Scripts are scratch artifacts.

Facts: center sampling is the best of the family yet still leaves 93 mismatches with maxerr 117 across the six probes, against 78 for the unsnapped exact-area baseline. Top/bottom sampling exceed 119 mismatches with errors to 244. Snapping slopes to 1/8 or 1/16 strictly worsens the exact-slope span result (70-71 versus 62 over four probes). The family fails structurally wherever an edge crossing moves within a row, worst on fractional phases.

Decision: per-row single-sample spans, with or without endpoint or slope quantization, are eliminated. What remains open is an area-accumulating edge walk whose fixed-point accumulation must reproduce exactly the observed small mixed deltas, or the actual mask-writer algorithm identified from the pinned Skia revision and verified black-box against the hold-out. No implementation change; the diagonal lowering gate is unchanged.

## Per-pixel pattern and grid-phase elimination (2026-09-23 decision analysis)
Question: what per-pixel structure do the exact-area residuals have, and does any uniform sampling grid (at any phase) reproduce the hold-out? The full base-probe delta table under exact area plus round-half-up is: (1,1) 211 vs 213, (2,1) 192 vs 191, (6,1) 20 vs 21, (1,2) 192 vs 191, (1,3) 152 vs 149, (1,4) 104 vs 106, (1,6) 24 vs 21. Converted to required coverage errors these are -0.004, +0.001, -0.003, +0.001, +0.011, -0.007, +0.009: mixed signs with adjacent rows disagreeing (rows 3 and 4 need +0.011 then -0.007), which rules out any rigid edge shift or global bias.

Method: five retained transparent probes (base, mirror, 2x-scaled, fractional, half-grid). Tested per-row bounding spans (min/max crossings over the row, snapped to 1/16-1/256 or exact, column-clipped, round output) and uniform point grids (8x8 and 16x16 samples per pixel at every distinct subcell phase, round output). Scripts are scratch artifacts.

Facts: bounding spans overcover structurally (108 mismatches, maxerr 235, snapping changes nothing). Shifted uniform grids peak at 93 mismatches with maxerr 33 on 8x8 grids while 16x16 grids score worse; no phase fits, so the earlier unshifted-supersampling rejection extends to all phases. Together with the eliminated vertex, slope and span families, the survivors must compute per-pixel coverage with per-pixel (not per-row, per-vertex, or per-grid) arithmetic.

Decision: bounding spans and uniform sampling grids at any phase are eliminated. The next candidates are per-pixel analytic formulations (for example fixed-point area arithmetic rounded per pixel) or the actual mask-writer algorithm identified from the pinned Skia revision and verified black-box. No implementation change; the diagonal lowering gate is unchanged.

## Pinned AAA mask-writer replication (2026-09-24 decision analysis)
Question: does the pinned Skia analytic-AA mask writer reproduce the diagonal hold-out exactly? The Chromium tag 153.0.8010.52 DEPS was independently verified to pin Skia f8b66b7597c4cc859d3ed190e9c6872241e6721c, and the SkAnalyticEdge/SkScan_AAAPath sources were read at that revision (read-only; no code ported into the product). The algorithm: 16.16 fixed-point edges with table-quantized slopes, quarter-pixel Y snapping, 1/16 X snapping, and truncating fixed-point trapezoid/triangle coverage ops.
Method: a faithful integer-semantics replica was built as a scratch artifact (not retained, not ported): exact FDot6 input conversion, quarter Y snap, inverse-table slopes verified against the closed-form reciprocal, per-row 1/16 X snapping, full walker dispatch including the R==1 overload resolution that initially cost one pixel. Compared against all six retained transparent probes (576 pixels).
Facts: all four integer-vertex probes replicate exactly (base, mirror, winding-reordered, 2x-scaled: 448/448 pixels), as do all fractional left/middle/partial-row pixels. The sole residual is right-side two-pixel spans with first=second=0.5 device pixels and rDY=65536 (19 pixels across fractional/half-grid): the replica yields (223,32), the oracle (224,31). Forcing the triangle op to behave as rDY>>11=31 instead of 32 reproduces all six probes pixel-exact (576/576) without breaking any match, proving the residual is exactly this single unit. No matched pixel anywhere exercises that op with rDY>>11=32, so the data cannot yet separate an rDY-boundary input effect from a right-span formula difference.

Decision: the AAA mask writer is confirmed as the backend mechanism with one single-unit open item. Next: a scratch decision probe placing a right-side two-pixel span under a non-boundary rDY (shallow right edge) decides between an rDY-boundary input effect and a right-span formula difference. No implementation change; the diagonal lowering gate is unchanged pending that result.
## Faithful fDY closes the residual (2026-09-24 decision analysis)
Question: is the single-unit tri residual a transcription error? Re-reading setLine showed absSlope is the full-SkFixed |slope| with no FDot6 conversion, so every probe edge (slopes 6, 1, 1/6 in magnitude, all >= 1024 in SkFixed) takes the SkAbs32(quick_div(dy,dx)) branch, never the inverse table. The replica had wrongly quantized slopes to FDot6 before inverting. Decision-probe support: a fresh mirrored-fractional scratch capture (three runs, file-identical) isolates right-side spans under non-boundary rDY values.

Facts: with faithful fDY, all six retained transparent probes replicate pixel-exact (576/576) with no forcing, plus the decision probe (64/64), total 640/640. The previously forced values fall out naturally: fDY of the -1 slope is 65535 (quick_div truncation, not the table 65536), fDY of the -1/6 slope is 393216. Integer-vertex probes are unchanged (their exercised paths never touch the differing branch). Table extraction itself was verified value-by-value against the closed-form reciprocal.

Decision: the AAA mask writer is confirmed as the browser backend mechanism for diagonal fills on transparent backgrounds. Admission into the renderer still requires: white/black compositing with the recorded overshoot, overlap/layer-opacity compositing, slice-3 compound/curved geometry, and renderer integration preserving rectangle equivalence with work/cancellation/bounds accounting plus the full suite. No implementation change in this turn; the diagonal lowering gate is unchanged.

## Opaque-background compositing is exact (2026-09-24 decision analysis)
Question: does single-layer AAA coverage composite exactly over opaque backgrounds, and does the recorded white-background overshoot generalize beyond group-opacity paths? The white/black diagonal probes share the base triangle geometry with three-run stable references.
Method: per-pixel comparison of the white/black references against the exact replica alphas (base probe decoded straight as (255,0,0,A) everywhere). Predicted white (255,255-A,255-A) and black (A,0,0) with exact integer arithmetic, no rounding model.

Facts: zero anomalies across all 64 pixels times three images. White matches (255,255-A,255-A) exactly and black matches (A,0,0) exactly. The +1 white overshoot does not appear in this single-layer case, so it stays scoped to group-opacity paths (group-fade-mark) rather than general white compositing.

Decision: opaque-background compositing of single-layer AAA coverage is validated exact with no rounding model needed. The remaining compositing work is two-layer source-over (overlap probes) with the known coverage-dependent alpha question. No implementation change; the diagonal lowering gate is unchanged.

## Overlap compositing rule and blue-side residual (2026-09-24 decision analysis)
Question: what compositing arithmetic explains two-layer diagonal overlaps, and does the AAA replica extend to them? Fresh symmetric-layer captures (64x64 red-only, blue-only, composite; two runs each, file-identical) show both layers halve round-half-up to 128, while red-over-blue and blue-over-red composites both give alpha 191. That rules out asymmetric layers and confirms coverage-dependent compositing: identical (128,128) stacks give 191 full-canvas versus 192 on partial triangle pixels.

Method: exact replica coverages feed layer alphas ((A+1)//2, proven on 17 single-layer pixels plus disjoint-probe ratios), then a shared per-pixel rounding switch (round alpha and red-premult iff either layer fully covered, else truncate; unpremultiply always rounds) across four overlap-family probes plus the fresh rectangles.

Facts: the rule reproduces the 8x8 overlap (64/64), both disjoint layer probes, and both fresh rectangle orders exactly. The half-grid overlap keeps 5 blue-side pixels (row-1 sliver needing mask 1-2 from tri 0; rows 2-4 needing +2-3 opaque units, a uniform +0.012 coverage shift on the blue left edge). Sweeps over X-snap modes/grids/tiebreaks, tri rounding, Y-snapping, and fDY variants show the status-quo transcription uniquely minimizes mismatches; no global variant fits. Notably the red half-grid triangle in the same image is fully exact, so the residual is path-dependent, not coordinate-dependent.

Decision: layer quantization ((A+1)//2) and the shared-switch compositing rule are admitted as calibrated findings for partial overlaps; full-canvas layers keep the truncating path per the retained 191. The 5-pixel blue-side residual stays open with a crisp next step: capture the blue half-grid triangle alone to decide whether its +0.012 shift is coverage-intrinsic (single also off) or compositing-contextual. No implementation change; the diagonal lowering gate is unchanged.

## Blue-side residual is coverage-intrinsic (2026-09-24 decision analysis)
Question: is the 5-pixel blue-side residual coverage-intrinsic or compositing-contextual? Fresh scratch captures of the blue half-grid triangle alone, opaque and half-opacity (three runs each, file-identical), show the same 5 mismatches (opaque +1 to +4 units), so the shift lives in single-triangle coverage, not in overlap compositing. The half-opacity version halves consistently, confirming the layer rule independently.

Method: the opaque blue capture was compared pixel-by-pixel against the faithful replica (zero mismatches everywhere else on this triangle). A combined sweep over tri rounding (truncating vs +128) and X-snap tiebreaks (half-up, half-even, half-down) across all five retained single probes plus the blue triangle shows the source-faithful combination (truncating tri, half-up snap) uniquely minimizes mismatches; every alternative breaks matched pixels.

Facts: all 5 residuals sit on the blue left edge in apex-adjacent partial rows and read ~2x the replica tri value (mask 1-2 vs 0 computed from 0.75; mask 7-8 vs 4 computed from 4.35), suggesting the partial-row triangle helper undercounts when the row height is fractional. The red triangle in the same image and all other probes are fully exact, so the effect is path-dependent (apex partial-row edge spans), not a global rule error.

Decision: the residual is localized to partial-row triangle exclusions on one edge configuration; the next step is a scratch probe isolating a partial-row edge span (fractional apex with a shallow edge) to test partial-row dY handling directly, rather than more global variant sweeps. No implementation change; the diagonal lowering gate is unchanged.

## Full hold-out tabulation (2026-09-24 decision analysis)
Question: what is the exact validation state of the calibrated diagonal pipeline (faithful AAA coverage, round-half-up layer halving, shared-switch compositing, exact opaque compositing) across every retained probe?

Method: a single tabulation run compares the replica plus compositing rules against all twelve probes.

Facts: zero mismatches on all six single triangles, the 8x8 overlap, both disjoint layer probes, and both opaque-background probes, plus the mirrored-fractional and symmetric-rectangle decision probes. The half-grid overlap keeps exactly 5 blue-side pixels (row-1 sliver needing mask 1-2 from tri 0; rows 2-4 needing +2-3 opaque units, a uniform +0.012 left-edge shift). Fresh single-triangle blue captures prove the shift is coverage-intrinsic. All global snap, tiebreak, rounding, and fDY variants were swept; the source-faithful transcription uniquely minimizes mismatches.

Decision: the calibrated pipeline is complete except the 5-pixel blue-side residual, which is now bounded (one probe, one edge configuration, maxerr 4 opaque units) with its discriminating follow-up defined (single blue triangle already captured; apex-strip partial-row dY handling is the prime suspect). Renderer integration may proceed with the diagonal gate held until the residual clears, since every other family is exactly characterized. No implementation change in this turn.
## Cubic dicing reduces to calibrated lines (2026-09-24 decision analysis)
Question: does the pinned cubic pipeline (builder y-extremum chop, forward-difference dicing, per-segment updateLine with SkFDot6Div slopes) reduce the retained cubic arch to the already-calibrated line machinery? The arch is a single symmetric cubic plus chord, y-symmetric so its extremum is exactly t=0.5 with dyadic chop points.

Method: faithful integer-semantics replica of setCubicWithoutUpdate, updateCubic resumption, and updateLine (FDot6-abs fDY rule, truncating SkFDot6Div slopes, SnapY pinning) as a scratch artifact. The two halves dice to 8 line segments total, fed to the proven strip walker with no changes. Compared against the retained 8x8 blue arch reference.
Facts: zero mismatches across all 64 pixels. Notably the dicing is slightly asymmetric between halves (fixed-point forward differencing) while the oracle is perfectly mirror-symmetric, so symmetry is restored downstream by snapping and span logic, not by the dicer. The diced slopes use exact division, unlike line edges which use the inverse table.

Decision: cubic curves are admitted as diced line soup for the calibrated walker; the only verified case is symmetric with an exact dyadic chop. Still open: asymmetric/general cubics (float chop rounding), quadratics, arcs/circles, curve-curve intersections in scanlines, and the walker convexity assumption beyond triangles. No implementation change; curve geometry stays gated.

## Circle primitive unidentified (2026-09-24 decision analysis)
Question: which path primitive does the retained 6x6 circle take to the calibrated walker?

Method: candidates tested as faithful replicas: kappa quarter-cubics (all start angles and both directions), kappa octant-cubics, quarter-conics to quads (pow2=0 verified robustly), octant-conics to quads, tangent quads, exact-area circles/polygons (16/64/360-gon), and shifted/scaled circle fits. The oval emission was confirmed as move plus 4 conics in the pinned source.
Facts: best is kappa-quarters at 11 mismatches (maxerr ~13, asymmetric top-under/bottom-over with left-right skew); conic routes give coarse 128/0 patterns (quad dicing at shift 1 deviates ~1px); exact area of any circle (including shifted/scaled fits) fails at 10+ mismatches; MSAA grids and supersampling are excluded by value analysis. Diagonal-join quads are structurally impossible (their top/bottom pieces are zero-height after rounding and would be skipped, leaving holes the oracle does not have). The walker itself was proven left-right symmetric on mirrored segments, so the observed replica asymmetries come from forward-difference dicing, while the oracle is perfectly symmetric.

Decision: the circle primitive stays unidentified with all determined candidates eliminated; the quad dicer, conic machinery, tolerance/subdivision code, and oval emission structure built along the way are retained as calibrated tooling. The circle stays gated. Next candidates if revisited: Blink-side circle-to-path emission (which selects conics vs cubics vs segmentation before Skia), and per-direction dicing-pairing interactions.

## Apex partial-row probes narrow the blue-side residual (2026-09-24 decision analysis)
Question: does the blue-side residual reproduce in an isolated fractional-apex configuration, or is it specific to the half-grid overlap triangle? The residual (5 pixels, maxerr 4 opaque units) sits on the blue left edge in apex-adjacent rows; the prime suspect is partial-row triangle dY handling.
Method: two retained 8x8 probes with three-run identical references, both still rejected at the diagonal lowering gate: diagonal-blue-apex-8x8 keeps the exact blue half-grid triangle alone and opaque, promoting the earlier scratch capture to tracked evidence; diagonal-shallow-apex-8x8 is a right triangle with fractional apex (4.40625,1.40625), a shallow apex edge, and axis-aligned legs, isolating a two-edge partial top row. Pinned round-half-up analytic-area comparisons record each oracle signature (blue 22 changed/maxerr 24 with the 255 channel spike on the (6,6) sliver; shallow 19 changed/maxerr 26).
Facts: row 1 reads below analytic in both probes (blue 1/30/73/82 versus 13/54/97/98; shallow 10/68/50 versus 30/94/56). Left-edge pixels below the apex read above analytic with repeated absolute offsets (+19 on blue (1,3),(2,4),(3,5),(4,6),(5,7) and shallow (2,5); +11 on blue (2,3),(3,4),(4,5)). The same directional family appears in the isolated two-edge configuration, so the effect tracks the apex and partial-row configuration rather than the slope-1 left edge or overlap compositing specifically. The replica run needed to adjudicate the exact remainder is not available in this checkout.
Decision: the residual stays open with a tighter bound. Any partial-row dY candidate must reproduce both new oracle tables plus all twelve existing probes, and the replica comparison still has to confirm the exact remainder. The diagonal lowering gate is unchanged. Next: run the faithful replica against the two new references and test the partial-row dY variant directly.

## Blue-side residual survives global variants; grid maps the solution manifold (2026-09-24 decision analysis)
Question: which calibrated-pipeline variant clears the 5-pixel blue-side residual (rows 1-2, plus 1 to 4 units) while keeping the other thirteen probes exact?
Method: a parameterized 14-probe harness (six singles, both overlaps with the shared-switch compositing rule, both layer-opacity probes, both opaque backgrounds, plus the two new apex probes) over replica-module variants, plus a positional grid search over row-2 blit inputs using the verified R2 ops, plus source re-reads (convex-walker selection, setLine and FDot6 conversion, strip subdivision, HALF handling). Scratch harness and grids: artifacts/zz-final.py, artifacts/zz-grid.py, artifacts/zz-grid2.py, artifacts/zz-search.py, artifacts/zz-fracV1.py through zz-fracV4.py.
Facts: the status-quo baseline reproduces the tabulation (twelve zeros, blue-apex 5, shallow-apex 0). Eliminated with full-harness evidence: exact-vertex-ys subdivision (fractional 14, half-grid 15); HALF gated on full rows (fractional 3, half-grid 9, shallow 8, overlaps worse, blue still 5); tops-bare with bottoms-halfed (base, mirror, rotated, size16, white and black all break); accumulator-carry removal (bit-identical output, hence a no-op). Source confirms the replica transcription on strip subdivision (integers plus snapped endpoints via the plus-one-unit ceil idiom), uniform per-segment HALF add and subtract, all alpha primitives and accumulation loops, setLine conversion for the probe values, and convex-walker selection for single triangles. Grid result: with right side, slopes, heights and op structure fixed at replica values, exact row-2 totals force seg1 left top 245760 with bottom at most 49152, and seg2 left (49152, 81920): row-2 left positions one snap unit left of the replica with row-1 and row-3+ fixed. Closed-form analysis shows the needed outputs are overconstrained under the verified formulas: no tri rounding (the 8-versus-4 requirement is non-monotonic on shared dY), no alpha_below input adjustment (chain incompatibility across the three pixels), no fullA change (breaks row 1), no per-strip dY change. The replica accumulator sits exactly on snap boundaries here (249856 is 61 times 4096) yet init and stepping formulas leave no room for the required thousands-scale shift, and any single carried value contradicts row-1 versus row-2 simultaneously.
Decision: the residual stays open; the diagonal lowering gate is unchanged. The defect is not a global rule (every global family eliminated with evidence) and is not reachable by input rounding within the verified formulas. It needs the goY stepping formula or the joint-switch fX handling, both in the unextracted SkAnalyticEdge header, or a structural subtlety in R2 dispatch under these inputs. Next: transcribe goY and updateLine stepping for lines, or instrument the joint switch directly; re-run the 14-probe harness per candidate.

## Blue-side hunt: pinned header closes verification gaps, manifold refined (2026-09-24 decision analysis)
Question: what remains unverified in the replica transcription, and what is the exact shape of the needed row-2 values?
Method: fetched the pinned SkAnalyticEdge.h read-only at the verified revision (goY both overloads, kDefaultAccuracy 2, SnapY form); diffed the embedded inverse table against the replica table; read SkFixed.h semantics, setLine branches, all R2 and blitter branches, and the convex-walker HALF symmetric add and subtract. Ran a wide positional grid over row-2 blit inputs plus FAM-A and FAM-WILD joint decompositions. Scratch: artifacts/zz-final.py, zz-grid.py, zz-grid2.py, zz-search.py, zz-detail.py, zz-acc.py.
Facts: goY stepping is floor-equivalent to the replica fmul stepping, so stepping cannot discriminate (any single-unit account fails by thousands where buckets force it). SkFixed Ceil, Floor, Mul, Round and saturating ops all match the replica assumptions over the probe value ranges, including negatives where applicable. Both setLine conversion branches coincide for the probe values. The inverse table diff is empty (0 of 1025, including the entries backing blue edge slopes). The ln==1 overwrite-versus-add split matches the maskRow logic exactly, as do blit_two_alphas and blit_full_alpha (always additive). Triangles take the convex walker and its HALF handling nets to the replica carry. The wide grid completes the solution manifold: with right side, slopes, heights and op structure fixed, exact row-2 totals need seg1 min 245760 with max in 45056 to 49152 and seg2 (49152, 81920), including swapped-doppelganger families that are geometrically absurd and a FAM-WILD alternative needing a far bottom. Minimal-deviation solution is rigid minus 4096 except the seg2 bottom. Tri rounding is impossible (non-monotonic 8-versus-4 on shared dY), alpha_below input adjustment is impossible (chain overconstraint across the three pixels), single carried values contradict row-1 versus row-2 simultaneously, and knife-edge flips need thousands where rounding gives ones. Separately, overlap rows 3-4 (plus 2 to 3) cannot be byte-level coverage given exact single-blue rows 3+: that part must be compositing amplification and is a separate follow-up, narrowing the coverage scope to rows 1-2.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Every globally uniform mechanism is now verified or eliminated with evidence; the needed row-2 values are row-2-specific under all verified formulas. Next: a micro-probe family varying the switch-vertex exact y across the snap boundary to map when the residual appears, then joint-switch fX instrumentation; re-run the 14-harness per candidate.

## Switch-vertex family shows non-monotonic residual; yShift clamp eliminated (2026-09-24 decision analysis)
Question: does the blue-side residual track the switch-vertex snap bucket, and does the yShift quarter-clamp explain partial-row strips?
Method: two new retained 8x8 probes moving A along the same slope-1 left edge (apex, C, fill fixed) so only the switch row changes: diagonal-switch-225-8x8 (A snaps 2.25) and diagonal-switch-275-8x8 (A snaps 2.75), each with three-run identical references, pinned analytic comparisons (225: 22 changed, maxerr 24 with the 255 sliver spike; 275: 21 changed, maxerr 26 with the same spike) and replica comparison. Also screened five byte-level input variants of the blue triangle (vertex order, explicit close, lowercase z, polygon element, extra decimals): all five capture byte-identical to the retained blue reference. Also ran a yShift-clamp variant splitting 0.75 strips into 0.25 plus 0.5 against the full harness. Scratch: artifacts/zz-final.py, zz-sw.py, zz-apex3.py, zz-fracV5.py, zz-detail.py.
Facts: replica mismatches are 2 (225: row-1 (4,1) plus 1, row-2 (0,2) plus 9), 5 (blue at 2.5) and 0 (275) — non-monotonic in switch snap, so the residual does not track the bucket alone. The yShift-clamp variant breaks six previously exact probes (fractional 6, half-grid 5, both overlaps, both layer-opacity probes) while leaving blue at 5 and shallow at 0: the clamp as implemented is refuted, and blue has no quarter strips anyway. Input screening proves Blink delivers the assumed geometry (no winding, close-form, case, element or precision normalization). Rows 3 and below keep the identical left-edge signature with the replica exact in all three probes, so the active region stays rows 1-2 with the switch inside row 2. The 275 probe is exactly clean despite asymmetric 0.75 plus 0.25 strips, which constrains any subdivision theory to preserve it.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Next: a denser switch sweep with probe pairs sharing each snap bucket at different exact offsets (2.03125, 2.28125, 2.53125, 2.78125, 2.90625 on the same edge line) to separate bucket tracking from exact-offset tracking, then joint-switch fX instrumentation on the discriminating pair.

## Joint-value continuity reframes the residual; input screening passes (2026-09-24 decision analysis)
Question: what single model explains the needed row-2 values together with exact row-1 and rows 3+?
Method: full-provenance execution traces (every R2 and blit call with inputs) for blue versus the switch family; five byte-level input variants of the blue triangle (vertex order, explicit close, lowercase z, polygon element, extra decimals) captured and hashed; joint grid solutions re-examined for cross-row consistency. Scratch: artifacts/zz-prov.py, zz-detail.py, zz-acc.py, zz-grid2.py.
Facts: all five input variants capture byte-identical to the retained blue reference, so Blink delivers the assumed geometry and the gap is in walker mechanics, not input normalization. Provenance confirms the analysis model (row-1 and seg1-row2 blits share identical (53248, 249856) inputs with different x-offsets). The consistent solution is joint-value continuity: TRUE accL at y=2.0 in 243712 to 247807 serves row-1-bottom (249856 masked) and seg1-top (245760 masked) simultaneously, and TRUE nL at y=2.5 serves seg1-bottom and seg2-top simultaneously, so no single carried value is contradicted once masking is per-strip. The remaining puzzle is narrow: row-2 strips need minus-4096-equivalent blit inputs with row-1 and row-3+ fixed, i.e. dying-edge (A to B) and birthing-edge (A to C) positions shift while surviving-edge and walk-start positions hold. All uniform masking, rounding, subdivision, acc-carry and fullA families stay eliminated; the shift must be joint-local.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Next: model joint-local position handling explicitly (fresh versus carried fX at switches, per-strip masking phase) and validate against the 14-harness plus the switch family; the micro-probe pair design from the prior entry still stands for separating bucket from offset tracking.

## Dense switch sweep excludes buckets; table path confirmed (2026-09-24 decision analysis)
Question: does the residual track the snap bucket or the exact switch geometry, and does any quick_div input take the exact-division fallback?
Method: five more retained 8x8 probes on the same slope-1 edge line (A.y 2.03125, 2.28125, 2.53125, 2.78125, 2.90625; snaps 2.0, 2.25, 2.5, 2.75, 3.0), each with three-run identical references and manifest gating, completing an 8-point sweep with the earlier three. Replica comparison over the sweep plus accumulator bucket-position tables and a re-read of quick_div bounds. Scratch: artifacts/zz-swnew.py, zz-buckets.py.
Facts: replica mismatches across the sweep are 0, 2, 0, 5, 0, 0, 0, 0. Same-bucket pairs diverge sharply (2.25 bucket: 2 versus 0; 2.5 bucket: 5 versus 0), so the residual tracks exact geometry, not the snap bucket; integer-boundary switches (2.0, 3.0) are clean. kMaxAbsA is 4096, so every probe edge takes the inverse-table path and no exact-division fallback applies. Accumulator bucket positions show no clean knife-edge rule (exact multiples appear in both residual and clean probes). All five new probes are replica-exact, so no new analytic pins were needed beyond manifest retention.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Bucket tracking, fallback division and boundary-knife-edge theories are eliminated with evidence. Next: rotate the left-edge slope with apex and top edge fixed to test slope-1 specificity, then joint-switch fX instrumentation on the discriminating pair.

## Left slope is irrelevant; residual deltas are constant (2026-09-24 decision analysis)
Question: does the residual need the slope-1 left edge (whose fDX equals F1 minus one)?
Method: three retained 8x8 probes moving C with apex A, apex B and the top edge fixed, giving left slopes 0.6, 0.8 and 1.1 (plus blue at 1.0), each with three-run identical references and manifest gating. Replica comparison over the family. Scratch: artifacts/zz-slope.py.
Facts: all three new probes show exactly the same five residual pixels as blue: row-1 sliver plus 1, rows 2 plus 3, plus 2, plus 3 with identical values ((1,2) is 154 versus 151, (2,2) 203 versus 201, (3,2) 245 versus 242 in every case); only the far-left sliver (0,2) varies with slope (plus 7, 6, 4, 4) as its absolute coverage shifts. The B to C right edge also varies across the family (slopes minus 0.5 to plus 0.08) with no effect on the five. So the residual deltas are constant while absolute positions move with geometry: the defect lives in the top-edge plus switch-row machinery with left and right edges as passive fill providers. Slope-1 specificity, fDX boundary effects and input normalization are eliminated as explanations.
Decision: the residual stays open; the diagonal lowering gate is unchanged. The constant-delta signature (plus 1, plus 3, plus 2, plus 3) across four left slopes and both right-edge signs is now the sharpest characterization: any candidate mechanism must reproduce those exact deltas while keeping the thirteen exact probes and the eight switch-sweep probes at their observed values. Next: joint-switch fX instrumentation against this invariant, or the denser same-bucket pairs already queued.

## Joint-switch instrumentation eliminates fX, routing and birth-phase families (2026-09-24 decision analysis)
Question: which joint-local mechanism produces the minus-4096 row-2 blit inputs: carried-versus-fresh edge positions, span-formula routing, or accumulator birth phase?
Method: provenance traces of strip handoffs (fresh versus carried fX and acc for dying, birthing and surviving edges) for blue against the clean 275-switch probe; then four single-mechanism replica variants over the full 14-harness, each a full-module copy of the status-quo transcriber. Scratch: artifacts/zz-joint.py, artifacts/zz-joint2.py, artifacts/zz-fracV6.py, artifacts/zz-fracV7.py, artifacts/zz-fracV8.py, artifacts/zz-fracV9.py.
Facts: working-state hygiene first: artifacts/aaafrac.py is the status quo (twelve zeros plus blue-apex 5, overlap-half-grid 5 reframed as compositing); artifacts/zz-fracV5.py is a losing variant (fractional 6, half-grid 5, overlaps worse) and must not anchor comparisons. Handoff traces show carried fX equals fresh fX at every switch top for both probes (blue survivor 433494 both, 275 survivor 430763 both; dying final equals birthing init at the apex x in both), and the dying edge final acc coincides with the birthing fresh accStart (53248 both) because both equal the apex x. V6 (always-fresh fX) is bit-identical to baseline on all eight single masks (zero differing entries), so edge positions have no freedom anywhere: the fresh-versus-carried family is fully eliminated. V7 (R2 ln==1/ln==2 fast paths forced to general accumulation) moves fractional and half-grid by exactly plus 1 each at (1,2) (133 to 134) while blue stays byte-identical on all five residual pixels: fast-path routing is load-bearing elsewhere but eliminated for the residual. V8 (blitter narrow-span specials forced to general loops) moves half-grid minus 1 at (2,6) (193 to 192), overlap-half-grid plus 1 and layer-half plus 1 while blue stays byte-identical: narrow-span formulas are load-bearing elsewhere but eliminated for the residual. V9 (fresh-edge acc init without HALF past the first strip) breaks mirror, rotated, half-grid, shallow, both overlaps and both layers (5, 5, 11, 4, 10, 15, 10, 12) while blue stays at 5: birth-phase freedom is refuted broadly and HALF-on-fresh is structural.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Positions (V6), span-formula routing at both levels (V7, V8) and birth phase (V9) are eliminated with full-harness evidence; the residual is invariant under every joint-handoff perturbation tried. What remains untested in the joint space is continuing-edge acc re-masking at strip starts and R2 degenerate dispatch (ll-greater-lr collapse, ul/ur swaps). Next: one of those two, or the compositing follow-up for overlap rows 3 to 4.
## Residual onset is discontinuous along the edge line; branch paths identical (2026-09-24 decision analysis)
Question: does the blue residual fade gradually or switch discretely as the apex moves along its edge line toward the clean 250b configuration, and do taken-branch paths differ across the boundary?
Method: three scratch 8x8 probes with A on the blue edge line (x equals y minus 1.625) at A.y 2.4375, 2.46875 and 2.5, each with three byte-identical pinned-Chrome captures; replica comparison for each; R2 branch-path instrumentation (collapse, swaps, early-out tags per row_call) for blue versus the nearest clean probe. Scratch: artifacts/zz-onset-24375.svg, artifacts/zz-onset-246875.svg, artifacts/zz-onset-250.svg, artifacts/zz-joint3.py.
Facts: blue at 2.40625 shows the known five (row-1 sliver plus 1, row-2 plus 3, plus 2, plus 3); all three intermediates show zero mismatches, so the residual switches off discontinuously within one 1-32 step of apex y and stays off through 2.53125. Branch paths are identical row-for-row across the boundary (swapL plus swapR on rows 1 and 2 first call, swapR thereafter); the only difference is values: strip-2 row-2 ll reads 81920 for blue versus 86016 for the 2.4375 probe (plus 4096 persisting down rows 3 to 6, reconverging at row 7), while strip-1 row-2 inputs are byte-identical. The discriminating input difference is the birthing-edge table entry (dx64 320 versus 318 with dy64 320); strip structure, snap buckets and branch selection are the same on both sides.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Gradual-drift and branch-selection explanations are eliminated for the onset: the defect localizes to the strip-2 row-2 left-span value computation with a one-quantum flip between adjacent exact geometries. The probes stay scratch; retain the boundary pair in the manifest only if future harness work needs them. Next: branch conditions on unmasked operands, or table-entry rounding analysis for dx64 318 versus 320.
## Exact rationals kill the dx hypothesis; swap grid proves structural gap (2026-09-24 decision analysis)
Question: does table-entry rounding (dx64 320 versus 318) or some (ul, ll, lDY) combination explain blue row 2, and what does truth compute for the unity-slope edge?
Method: exact-rational audit of the strip-2 row-2 fixed-point chain for blue versus the clean 2.4375 probe; source read of the pinned quick_div and setLine; exhaustive swap grid over the differing row-2 inputs (strip-1 lDY, strip-2 ll and lDY) against oracle row 2. Scratch: inline python with fractions plus the 14-harness.
Facts: exact nL for blue strip-2 row 2 is 86016, exactly on a snap boundary, while the replica fmul truncation (32767 versus 32768) lands one below at 86015 and masks a full quantum lower (81920 versus 86016); the clean probe has 1843 units of margin so the same minus-one error is harmless there. But the pinned quick_div source takes the table path for these entries (abs_b 320 within 8 to 1024, abs_a 320 below 4096), and the inverse table diff is empty, so truth computes the same 65535 and the dx hypothesis dies at source level. The swap grid then shows no combination of the differing inputs reaches oracle row 2 [8, 154, 203, 245]: x=0 sticks at 4 (needs 8), x=2 and x=3 stick at 201 and 242 (need 203 and 245) because strip-1 paints them through its swapped span, and lDY moves go the wrong way. A ul sweep (57344 to 45056) brackets x=0 between 1 and 10 without ever hitting 8. So the replica formula family cannot reach the oracle row from any nearby inputs: the difference is structural in the row-2 left computation, not parametric. The masked-versus-unmasked audit agreed everywhere, so it is not an operand-masking flip either.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Value-level (dx, fmul, acc, masked-versus-unmasked) and input-swap explanations are now all eliminated with exact evidence; what remains is a formula or branch-selection difference in truth for this narrow-span configuration, or a pinned-revision versus executed-backend mismatch (source revision alone never proved the backend). Next: micro-probes isolating each narrow-span formula against oracle predictions directly, or a targeted re-read of the pinned blitter narrow-span selection for these inputs.
## Blitter re-read verifies transcription; collapse formula diverges unfired (2026-09-24 decision analysis)
Question: does the pinned blitter source differ from the transcription on dispatch, narrow-span selection or formulas for blue strip-2 row-2 inputs (ul 53248, ll 81920, fullA 128)?
Method: line-by-line audit of the pinned SkScan_AAAPath.cpp blit_trapezoid_row, blit_single/two/full_alpha, blit_aaa_trapezoid_row, trapezoid_to_alpha, partial_triangle_to_alpha, compute_alpha above/below and approximate_intersection against the transcriber for those inputs.
Facts: dispatch, swaps, joins, ln==1/ln==2 selection and formulas, general accumulation loops, narrow-span specials and both alpha primitives all match; int32 overflow is impossible at these magnitudes (largest product 244-cubed range, fmul operands 7 below overflow) and C++ truncation versus Python floor coincide on the non-negative values used. One real divergence found: the ll-greater-lr collapse uses approximate_intersection (endpoint-ordered midpoint) in source versus an unordered midpoint in the transcriber (example 17 versus 22), but the branch never fires for blue or any residual row, so it cannot explain the five pixels. Special-case equivalence confirmed behaviorally already: V7 and V8 moved other probes while blue stayed byte-identical.
Decision: the residual stays open; the diagonal lowering gate is unchanged. The transcription is now verified twice for every path blue exercises (behavioral 12-exact plus textual audit), which promotes the pinned-revision-versus-executed-backend mismatch from fallback to prime suspect alongside an unread walker-level difference in strip-versus-row ownership. Next: narrow-span micro-probes isolating each formula against oracle predictions directly.
## Row-2 attribution grid proves unreachable oracle row (2026-09-24 decision analysis)
Question: which (ul, ll, lDY) inputs to the two strip calls reproduce oracle row 2, and what does the attribution say about the true computation?
Method: decomposed row 2 into strip-1 parts [0, 30, 73, 114] and strip-2 parts [4, 121, 128, 128] against oracle [8, 154, 203, 245]; dense sweep of strip-1 lDY (8500 to 11500) times strip-2 (ul, ll) over 62 combos, then a full 340-combo grid adding ul 45056 to 57344 and ll 69632 to 86016; ul-direction bracket for the x=0 pixel. Scratch: inline python grids against tracked references.
Facts: strip-2 x=0 needs (ul 49152, lDY 65945) for exactly 8, and (ul 49152, ll 77824) nails x=0 and x=1 exactly (8, 154) with strip-1 fixed, i.e. a uniform minus-4096 accL shift plus the neighboring probe lDY. But x=2 and x=3 need strip-1 parts (75, 117) while strip-1 lDY spans (73, 114) at 10922 through (82, 117) at 9000 moving both together: the needed pair sits on opposite ends and no dense lDY value yields it. The full 340-combo best stays at 3 diffs. So no inputs in a wide neighborhood reproduce oracle row 2: the true computation differs structurally, and any candidate must move strip-1 x=2,3 without a compatible lDY existing.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Input-side search is exhausted for row 2 (values, swaps, masking, branch paths, formulas all verified or gridded): the remaining candidates are executed-backend revision mismatch and strip-versus-row ownership of the split row. Next: narrow-span micro-probes isolating each formula against oracle predictions directly.
## Backend is absolute-position dependent; translation kills the residual (2026-09-24 decision analysis)
Question: is the blue residual a function of relative span geometry (formula-level) or of absolute canvas position?
Method: rigid plus-one-pixel translation of the blue triangle (zz-shift-px.svg, same 8x8 canvas), three byte-identical pinned-Chrome captures; replica translation check plus shifted oracle comparison, with confound audit (binary, recipe, script provenance, dyadic exactness, snap preservation, canvas clipping).
Facts: the replica translates perfectly (zero shifted diffs: relative inputs identical up to the uniform shift), but the oracle does not (five shifted diffs), and the translated probe matches the replica exactly over the full image: the residual vanishes under rigid translation. Confounds eliminated: same Chrome binary and recipe revision (provenance dates show the capture script postdates nothing relevant; transparent path unchanged), all coordinates dyadic-exact in every precision, snap/grid/branch relationships preserved by the integer shift, residual region interior with no canvas-clip engagement (full-image exact on the translated probe). Since fixed-point raster math is translation-invariant by construction (verified again by the replica zero-diff), the dependence must enter outside it: float32 SVG-to-path stages, Blink normalization, or canvas-relative operations at these magnitudes.
Decision: the residual stays open; the diagonal lowering gate is unchanged. This reframes the search: every position-independent formula theory is now suspect, including parts of the elimination chain that assumed translation invariance. The defect behaves like a magnitude- or position-sensitive stage, not fixed-point raster math. Next: narrow-span micro-probes at fixed absolute position to test formula fidelity independent of position, wider-canvas translations for periodicity, and a Blink-side precision audit.
## Position map shows x-singularity with y-invariance; RGB clean (2026-09-24 decision analysis)
Question: does the residual track absolute x, absolute y, or pixel-grid phase, and is compositing involved?
Method: rigid translations of blue (identical relative geometry): x by minus 1/2, minus 1/16, plus 1/16, plus 1/4, plus 1/2, plus 1 pixel, and y by plus 1 pixel (taller canvas), each with three byte-identical pinned-Chrome captures plus replica comparison; full-RGBA read of the residual pixels. Scratch: artifacts/zz-shift-*.svg and captures.
Facts: apex-x map reads clean, clean, DIRTY (5), clean, clean, clean, clean from 0.28125 to 1.78125: the residual fires at exactly one tested absolute x out of seven, narrower than plus-or-minus 1/16 pixel on both sides. The plus-1 y translation preserves all five values shifted by one row, so the dependence is x-specific with y-invariance. Residual RGB is pure blue on all five pixels, so compositing is sane and the defect is alpha-only. Fixed-point raster math is translation-invariant by construction (replica proves zero shifted diffs), coordinates are dyadic-exact at every precision, and snap, branch and formula inputs are preserved or provably equivalent across the map: no modeled stage can produce the flip.
Decision: the residual stays open; the diagonal lowering gate is unchanged. The defect is a singular x-position effect narrower than one snap quantum with y-invariance and clean RGB: remaining suspects are Blink path building at these magnitudes, canvas-origin-relative handling, or an unknown backend selection, all outside the verified fixed-point walker. Probes stay scratch. Next: fixed-position narrow-span micro-probes, or a direct Blink-output comparison if an instrumentation path opens.
## Toolchain verified; residual locked to apex-x with sliver doubling invariant (2026-09-24 decision analysis)
Question: is the oracle evidence itself trustworthy, and what exact configurations carry the residual?
Method: independent ffmpeg decode of the blue reference compared byte-for-byte against the harness reader; three micro-probes varying apex position (m1, m2, m3) with three identical captures each; full-RGBA audit of residual pixels; arithmetic decomposition of the sliver shortfall.
Facts: ffmpeg agrees with the harness reader on all 256 bytes, so the oracle values are real and the whole evidence chain stands. The residual fires if and only if apex-x equals 0.78125 across six configurations (blue, m1 at A.y 2.28, m2 at 2.53 with byte-identical five-pixel signatures; slopes with fixed apex), and stays clean for every other tested apex-x including plus-or-minus 1/16 neighbors and plus 1 rigid. The (0,2) sliver shortfall reads 8 versus 4 in all three dirty configurations: exactly double, stable across different strip structures. Residual RGB is pure blue, so compositing is sane. Arithmetic shows no single input tweak reaches 8 (no-HALF acc gives 7, doubled lDY is table-absurd, ul sweep brackets 7 against 12), and both the ln==2 fast path and the general accumulation give 4: the sliver formula with these inputs cannot produce the oracle value under any verified rule.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Apex-x locking plus sliver doubling plus RGB cleanliness is now the sharpest signature: any candidate must fire only at apex-x 0.78125, double exactly one sliver formula output, and leave RGB untouched. Next: fixed-position narrow-span micro-probes against oracle predictions, or Blink-side precision evidence at these magnitudes.
## 225 correction holds; span-interaction table narrows trigger further (2026-09-24 decision analysis)
Question: does the real 225-switch probe confirm the design record, and which (pixel, first) span configurations mismatch?
Method: reran the retained 225 triangle (A 0.53125, 2.15625, not the misremembered 0.625, 2.25) through the baseline: exactly the recorded two mismatches, so the shared baseline not drifted and the earlier five-diff run used a wrong triangle and is discarded. Then tabulated ln==2 left-span (pixel, first) outcomes across blue, m2, T1, qx, px16, mx16 and 225 plus swap-grid and mask-granularity probes.
Facts: mismatch if and only if pixel 0 meets first 12288 (blue, m2) or first 28672 (225, plus 11); all other tested combos match, including same-first at pixel 1 (T1, qx) and neighboring firsts at pixel 0 (px16 8192, mx16 16384). Equivalently mismatch matches accL bit-12 set at pixel 0 with the T1 pixel-1 control. Src-over compositing is eliminated by direction (it can only reduce sums; oracle exceeds them), and coarser masking moves toward oracle on x=0 but overshoots elsewhere and would break exact probes globally. Strip-ownership inversion was considered and rejected as geometrically absurd (paints dead edges past endpoints).
Decision: the residual stays open; the diagonal lowering gate is unchanged. The trigger is now a precise span-configuration predicate, but no verified rule produces the oracle values from it; the mechanism still needs backend visibility. Next: fixed-position narrow-span micro-probes, or Blink-side precision evidence.
## Cross-version stability kills recent regression; CTM and smooth-jump clean (2026-09-24 decision analysis)
Question: is the residual a recent backend regression, a transform-pipeline artifact, or a smooth-jump misfire?
Method: downloaded Chrome 132.0.6834.159 (chromium 1381561) to an isolated scratch cache with self-consistent hashes (pinned cache untouched), captured blue three times byte-identically, and compared against the 153 oracle and the replica; captured an identity-matrix-transform blue variant three times; evaluated the pinned smooth-jump conditions against blue edge values. Scratch: artifacts/chrome-old/, artifacts/chrome-old-pin.json, artifacts/zz-blue132-*.png, artifacts/zz-identity-xform-*.png.
Facts: Chrome 132 renders blue byte-identically to Chrome 153 on all 64 pixels while the replica shows the same five mismatches against both, so the behavior is stable across twenty months and three hundred thousand revisions: recent regression dead. The identity-transform variant renders byte-identically to plain blue, so Blink CTM application is clean here. Smooth-jump cannot fire for blue (kinked switch fails the slope-similarity test, end-check returns false), verified not argued.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Eliminated with running code: recent backend change, transform-pipeline rounding, smooth-jump misfire. What remains needs backend visibility (Blink path building at these magnitudes, canvas-origin handling) or accepts an ancient-stable-code difference. Next: fixed-position narrow-span micro-probes, or a direct backend-version behavior table if older milestones become relevant.
## Split-necessity dead; sliver shortfall independent of row kind (2026-09-24 decision analysis)
Question: does the residual need mid-row strip splits, and does the sliver shortfall depend on row kind?
Method: integer-switch probe A (0.78125, 3.0) with strips [1.5,3.0] and [3.0,7.5] (no row ever split by the switch), three identical captures; full per-strip decomposition of its rows versus oracle; wide-span fractional-switch probe as control.
Facts: the integer-switch probe shows seven mismatches, all oracle-above (row-1 sliver, row-2 plus 1/4/4/4/4, row-3 sliver plus 4): mid-row splits are not necessary, killing split-necessity outright. Its row-3 sliver (full row, fullA 255, strip 1) reads 7 versus 3 with the same (ul 53248, first 12288) configuration as blue strip-2 row-2 (partial row, fullA 128): the plus-4 sliver shortfall is independent of dY, fullA, strip, row and accumulation, determined by the span inputs alone. The wide-span fractional-switch control is fully clean. Apex-x 0.78125 now shows dirty across four A.y values (2.28, 2.41, 2.53, 3.0) spanning three strip structures.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Split composition, partial-row scaling and strip assignment are all eliminated as necessary conditions; what remains is a span-input-dependent shortfall (ul 53248 with first 12288 undercovers by exactly 4 in two independent row kinds) with no verified rule reaching oracle values. Next: fixed-position narrow-span micro-probes, or backend-visibility evidence.
## Swap-value audit scares clean; dy dispatch pinned to computed dx (2026-09-24 decision analysis)
Question: are the swap-grid lDY values (blue 10922, on24375 10979) actually correct, or carried over from other probes?
Method: direct Edge2 prints for both probes plus table reads (TABLE 640 negative 10922, 922 negative 41120, 960 negative 65536), then hand-recomputed every dy from the table to cross-check; caught and corrected three hand-arithmetic slips (snapped versus unsnapped endpoints, mis-subtractions) before any reached the record.
Facts: all machine values confirm: blue E0 dy 10922, on24375 E0 dy 10979, blue E2 dy 65535, split-int E0 dy 16383 (hand-verified via TABLE 640), qi(70) 59918. The dy dispatch keys on computed dx magnitude, not dx64: blue E0 dx negative 393216 takes the qdiv branch. Every swap-grid, dense-sweep and 340-combo input stands as run; no conclusion changes. Rule going forward: machine prints over hand arithmetic for all fixed-point claims.
Decision: the residual stays open; the diagonal lowering gate is unchanged. No new mechanism; this turn hardens the structural-gap evidence against input misattribution. Next: fixed-position narrow-span micro-probes, or backend-visibility evidence.
## Render entry verified; oracle stable across months (2026-09-24 decision analysis)
Question: which backend path renders our probes (mask versus runs, convex versus general), and is the oracle stable across sessions or drifting with machine state?
Method: read the pinned render entry (AAAFillPath selection, CanHandleRect math, bounds derivation, blitter classes, snapAlpha conditions); re-captured blue three times today and compared byte-for-byte and decoded against the retained March reference.
Facts: small-region probes take the mask blitter (CanHandleRect passes for 8x8 either as path or canvas bounds) with the convex walker for convex paths; bounds clamp to canvas/path edges that our interior spans never touch; fLeft-relative indexing is translation-invariant; snapAlpha applies only to the runs path and is refuted behaviorally by surviving unsnapped oracle values (row-1 sliver reads 1, never snapped to 0), doubly confirming the mask path. fmul truncation, partial_alpha rounding and saturating accumulation all match the transcription exactly. Today blue captures byte-identically to each other and to the March reference (same file hash, zero decoded diffs): the oracle is a deterministic pure function of input across months, killing session-state theories permanently; position dependence is therefore input-driven, not environmental.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Render selection, blitter classes, bounds, snap behavior and oracle stability are now verified facts constraining any candidate to input-driven backend logic. Next: fixed-position narrow-span micro-probes, or Blink-side precision evidence at these magnitudes.
## Compositing exonerated; overlap independently confirms blue masks (2026-09-24 decision analysis)
Question: is the overlap-half-grid residual (5 pixels) a compositing-math defect or inherited coverage, and does the harness composite rule match truth?
Method: recomputed the five overlap pixels with the harness composite rule fed by blue TRUE masks from the solo oracle (instead of replica masks), checking alpha and RGB jointly with zero free parameters.
Facts: with true blue masks, all five pixels close exactly: (3,1) needs blue true 1 versus 0 (composite then yields oracle 250, 0, 5, 49 exactly); (0,2) needs 8 versus 4; row-2 needs 154, 203, 245 versus 151, 201, 242, closing alpha and both RGB channels including through the a-equals-128 special case and round-half-up steps. The implied true blue row 2 matches the solo oracle pixel-for-pixel, so two independent contexts agree on blue values and compositing contributes zero residual. The harness half-opacity model, special case and premultiplied rounding are oracle-exact as transcribed.
Decision: the compositing follow-up is closed as working correctly; the full residual sits in blue coverage computation, now doubly confirmed. No mechanism change; gate held. Next: fixed-position narrow-span micro-probes, or backend-visibility evidence.
## Coverage is paint-independent; residual identical across paints (2026-09-24 decision analysis)
Question: does the diagonal residual (or coverage generally) depend on paint, which would implicate paint-side code paths?
Method: red and green variants of the blue triangle, three identical captures each; alpha-channel equality against the blue oracle plus exact paint-color check on every non-transparent pixel. Scratch: artifacts/zz-paint-red.svg, artifacts/zz-paint-green.svg and captures.
Facts: both variants show byte-identical alpha channels to blue (zero diffs, including all five residual pixels) with exactly correct paint RGB everywhere non-transparent. Coverage factorization holds exactly in the backend: same mask values regardless of solid paint, so the residual is purely geometric and any future coverage fix is automatically paint-universal for solid fills.
Decision: the residual stays open; the diagonal lowering gate is unchanged. Paint dependence eliminated with exact evidence; compositing was already closed last turn. Next: fixed-position narrow-span micro-probes, or backend-visibility evidence.
## Transparent gradient stops interpolate straight with undithered alpha and premultiplied-quantized dithered RGB (2026-09-24 decision analysis)
Question: what exact rule does the pinned oracle use for translucent gradient stops: straight or premultiplied ramp, dithered or exact alpha, which rounding, which dither phase?
Method: 64x16 red-opaque to blue-transparent objectBoundingBox ramp (artifacts/zz-fade.svg), three byte-identical fresh-process captures (artifacts/zz-fade-1/2/3.png, sha256 09a03c2d9556 prefix each) through the pinned recipe, then a full-grid fit over all 1024 decoded pixels against candidate rules using the retained Bayer table, plus phase-structure analysis of every mismatch.
Facts: captures are byte-identical, so the oracle values are stable. The ramp is straight sRGB: per-pixel alpha is linear and undithered (constant across all 16 rows at every column), and RGB follows premultiplied quantization with the existing table: premult byte is floor(lerpRGB times exact alpha over 255 plus table over 64), output is round-half-up of that byte divided by the separately rounded alpha over 255 (zero alpha yields zero). 962 of 1024 pixels match exactly with mean absolute error 0.37; every mismatch is a rounding-boundary pixel, mostly plus-or-minus 1. Two phase-(7,0) pixels at alpha 2 read 128 versus 0: a single-quantum premultiplied difference amplified by unpremultiplication, perceptually void and collapsing to about one step once composited over any background. At full opacity the rule reduces to the current InterpolateChannel expression (floor of straight plus table over 64), so admitted opaque output is preserved by construction; the full suite must still confirm that at implementation time. Row pairs repeat byte-identically with period 8 in both axes, confirming the shared table and phase. Centered dither with round-half-up premult is algebraically identical to uncentered dither with floor premult, so the shortlist collapses to one rule; truncated output, exact-alpha division, and straight-space evaluation all fit worse or contradict the retained opaque references.
Decision: transparent-stop implementation is specified (parse stop alpha through stop-color and stop-opacity, interpolate alpha undithered, premultiplied-quantize RGB with the shared dither, unpremultiply by rounded alpha, extend the multi-stop segment walk the same way); the diagonal gate is untouched because this is paint evaluation, not coverage. Next: implement in the gradient rasterizer, admit with pinned references and a census entry, and let the full suite prove opaque identity.

## ViewBox-only divergence is recipe layout, not product mapping (2026-09-27 decision analysis)
Question: is the whole-image known difference on viewBox-only files (inv-025 Bangladesh, inv-053 Luxembourg, inv-062 Yin yang) a product viewport-mapping defect or a capture-recipe layout artifact?
Method: rendered our inv-025 candidate at explicit viewports 500x300 (exact 10:6 aspect) and 300x180 (intrinsic-ratio height at width 300) with a throwaway Release runner under ignored artifacts/render-one/; captured fresh single-run oracles at both windows with the pinned recipe; compared with the retained VisualDiff tool; decoded the retained 300x150 oracle (b9-bangladesh-run1.png, byte-identical source to inv-025.svg) with a stdlib PNG probe (artifacts/measure-disc.py).
Facts: at 500x300 the diff is 786 px / max channel 45 / max alpha 0 boxed exactly to the disc region (x125-324 y50-249 against geometric x125-325 y50-250); at 300x180 it is 473 px / max 44 / alpha 0 boxed to x75-194 y30-149. Placement agrees to the pixel at both aspects; the residual is pure disc-edge coverage in the known S11 alpha-clean family. The 300x150 oracle decodes to red bbox x75-194 y30-149 (rx 60 ry 60, center 134.5, 89.5): uniform scale 30 with offset (0,0), so Chrome sizes the outer svg to width 300 and intrinsic-ratio height 180, top-aligned, cropping the bottom 30 rows: document layout, not image-intrinsic 300x150 plus meet. Our default (300x150, meet, xMidYMid) is the defensible image semantic; the two disagree by construction on any viewBox whose ratio differs from 2:1.
Decision: no product change; the mapping model is confirmed at two aspect points. Reclassifying inv-025/053/062 needs a recipe amendment (capture viewBox-only files at intrinsic-ratio windows, compare against explicit-viewport candidates) plus inventory and R31 sign-off; frozen entries stay as recorded. Next: propose the amendment; do not tune the product to document overflow.

## Intrinsic-ratio sittings: Luxembourg exact, Yin-yang trips granularity-sensitive simplicity gate (2026-09-27 decision analysis)
Question: does the recipe-layout account generalize across the viewBox-only family, and what blocks the arced member at its intrinsic ratio?
Method: explicit-viewport candidates at intrinsic-ratio sizes (inv-053 Luxembourg 300x180 for viewBox 0,0 5,3; inv-062 Yin yang 300x300 for -40 -40 80 80) from the throwaway runner; fresh single-run pinned oracles at both windows; VisualDiff comparison; stdlib extent probes (artifacts/measure-disc.py, artifacts/measure-extents.py) on oracles and candidates.
Facts: inv-053 at 300x180 is byte-exact (0 px), so its whole-image known difference was entirely recipe artifact. inv-062 renders at uniform magnification 1.875 (explicit 300x150: 4660 bytes, matching the retained default-size candidate; explicit 150x150: 4146 bytes) but rejects at 3.75 (explicit 300x300) through the polygon-simplicity gate. Mechanism: ScaledFlatness refines arc subdivision as magnification grows (0.25/3.75) and ClassifySet flags touching or intersection in the finer flattened S-spine polyline that the coarser version smooths over; the gate is granularity-sensitive, not scale-invariant. Oracle extent measurement at 300x300 gives opaque bbox x4-295 y4-295 (scale 3.7436, center 149.5,149.5), exactly our predicted map, and our 300x150 candidate measures scale 1.8718 center 149.5,74.5: placement agrees everywhere; admission is the sole blocker.
Decision: recipe amendment vindicated for rect and straight-path files and would promote inv-053 to exact under triples; for arced files it additionally requires R34 spatial winding/parity admission instead of granularity-sensitive rejection, per the existing route. Frozen entries stay as recorded. Next: triples for inv-053 under the amended recipe with inventory sign-off; R34 spatial stage for the yin-yang class.

## Fractional-curve gate removed; Albania renders, six advance to layered gates (2026-09-27 decision analysis)
Question: what happens when the fractional-curve admission gate is removed — which inventory family members render, and what are their next gates?
Method: removed the HasNonIntegralNumber gate in SvgOrthogonalShapeParser so curved paths lower through the shared ScaledFlatness device-map flattening; graduated the rejection theory into three admission pinning tests plus the objectBoundingBox-clip-on-curves test; re-probed seven family documents at Default options with the throwaway runner; ran the full core, visual and font suites.
Facts: 2316 core plus 100 visual plus 6 font tests green. Albania inv-081 renders (23797 bytes). Next gates after admission: inv-050, inv-067 and inv-070 reach polygon simplicity; inv-068 reaches integer transforms; inv-074 reaches inline stroke-width; inv-066 reaches fractional root dimensions. New tests pin a fractional quadratic sliver, an exponent overshoot triangle, a fractional cubic arch, and curve objectBoundingBox clip binding. No pixel exactness is claimed for newly rendering files; fidelity triples are pending.
Decision: admission without exactness claims per R27; the layered gates are now the measured backlog in priority order. Next: fidelity triples for inv-081; R34 spatial stage for the simplicity trio; transform, inline-width and dimension lanes for the rest.

## Fractional dimensions round to nearest device pixel; NASA worm renders with wordmark-region difference (2026-09-27 decision analysis)
Question: what output size should fractional root dimensions produce, and does rounding unblock the NASA worm?
Method: round the final scaled dimensions to nearest with ties away from zero in SvgConverter.Rasterize, keeping the fractional CSS size for viewBox mapping; graduated the rejection theory, added a four-case rounding theory including a halves pin and a round-last scale pin plus a Default-versus-explicit equivalence test; re-probed inv-066 at Default and compared against the retained 508x142 rounded triple; ran the full core, visual and font suites.
Facts: 2318 core plus 100 visual plus 6 font tests green. Root 508.204x141.732 renders at 508x142; the worm shows all counters composed with 3256 changed pixels, max channel 255, max alpha 146 across a whole-image box. Halves rounding is pinned for our output but unobserved against Chrome since no inventory document carries an exact .5 dimension. Non-positive rounded sizes and non-finite scaled sizes keep the existing UnsupportedFeature rejection, distinct from resource limits.
Decision: fractional-dimension admission without exactness claims per R27; inv-066 graduates to known-difference. Next: an exact-.5-dimension probe against Chrome to confirm the halves mode; white and black background variants for the worm if exactness is pursued.

## Simplicity-trio rejections are scale-invariant true topology, not granularity artifacts (2026-09-27 decision analysis)
Question: do the polygon-simplicity rejections on inv-050, inv-067 and inv-070 (reached after fractional-curve admission) come from magnification-sensitive flattening granularity like inv-062, or from true contour topology?
Method: explicit-viewport ladders with the throwaway runner: inv-050 at 2880, 1440, 720 and 360 square (magnification 1.333 down to 0.167), inv-067 at 1200x600, 600x300, 300x150 and 2400x1200 (0.25 to 2), inv-070 at 900x600, 450x300, 225x150 and 90x60 (30 down to 3); inv-062 as the control that flips between 1.875 and 3.75.
Facts: all three reject through the simplicity gate at every rung, both where ScaledFlatness is clamped at nominal 0.25 and where it refines down to 0.0083. No admission boundary exists for them, unlike the yin-yang control. The flags therefore reflect scale-invariant contour topology (letter-counter overlaps, flame self-touch, rotated-instance overlaps), not segmentation granularity.
Decision: no tolerance tweak admits this trio; they need the R34 shared spatial winding and parity stage per the existing route. Next: implement the spatial stage, using these three plus inv-062 as the acceptance set: inv-062 must keep rendering at all scales while the trio must render everywhere once spatial fill lands.

## Use-instance mapping reserves before allocating; definition ownership releases on all paths (2026-09-27 decision analysis)
Question: do unreserved instance-mapping arrays and unreleased definition reservations break the R39 ownership contract on success, failure and cancellation?
Method: reserve-before-alloc at eight Apply sites in SvgReferenceResolver through the document budget; flag-guarded ReleaseDefinitions called explicitly after scene build and from state teardown; state-owned DefinitionReservations list for definition-array shells; mirrored denial-and-release plus success-release regression tests.
Facts: pre-fix builds leaked 368 bytes (Scene category) on both the success and failure paths for a single triangle definition plus use: the definition array shell at 216 bytes with no owner plus payload reservations dropped with the table. Post-fix both paths release to zero; 2320 core plus 100 visual plus 6 font tests green with two new ownership tests.
Decision: R39 instance-mapping allocation hole closed and definition ownership holds on success, failure and cancellation. Remainder: instance-mapping work charging needs workCounter threading through the Apply dispatch; pre-Create retained, end and sign transient stays unaccounted; fan scans stay a charged cost.

## Instance-mapping work charged post-resolution at both entry points (2026-09-27 decision analysis)
Question: how is resolved use-instance mapping work charged without threading a counter through twenty Apply signatures?
Method: ChargeAppliedMappingWork sums polygon point counts over applied geometries and consumes one unit per point at the two resolution entries (immediate ResolveOrDefer and deferred ResolvePendingUses); pinned exact per-point accounting plus an end-to-end work-exhaustion denial test.
Facts: direct unit test charges exactly 3 units for a triangle plus rectangle batch (rectangles contribute zero); the denial test fails pre-change (budget suffices without the charge) and passes post-change; 2322 core plus 100 visual plus 6 font tests green.
Decision: mapping work accounting closed at entry granularity; per-loop incremental denial still wants counter threading. Next: thread the counter if a use-expansion denial case ever needs mid-mapping abort; otherwise leave the entry charge.

## Simplicity gate is pure conservatism for single-contour nonzero crossings (2026-09-27 spike analysis)
Question: does the ClassifySet simplicity gate protect rendering correctness, or only conservatism, and which contour-contact classes admit safely under the existing roles-plus-unsigned-area engine?
Method: neutered the three ClassifySet gates in SvgOrthogonalGeometryLowerer in scratch (all reverted afterwards; product code untouched in the final tree), ran the full core suite in Release, and probed the R34 acceptance set plus neighbors; second iteration with a narrow rule admitting only single-contour ProperCrossing under nonzero fill. Compared inv-058 and inv-067 candidates against their retained oracle triples with the VisualDiff tool; inspected the inv-067 candidate visually.
Facts: full neuter gives 2436 pass with exactly 15 failures, all gate-pinning (six Non_simple_diagonal_contours theory cases, Coincident_spike, Fractional_bowtie, Self_intersecting_diagonal_polygon, Touching_hole, one PublicContract bowtie case, one OpenTypeFont rejected-text agreement, two RootParser rejection cases, one fractional self-intersecting curve case); zero rendering regressions. inv-058 renders (37659 bytes) with an attributable alpha-clean residual (3997 px, max channel 101, zero alpha error, crescent and star bbox) against its retained 1200x800 triple. inv-067 renders a structurally complete Mongolia on inspection with a larger alpha-clean residual (25870 px, max channel 179, zero alpha error) still needing attribution. The narrow rule reproduces the inv-058 output byte-identically (same 3997/101/0) while inv-067 still rejects, isolating multi-contour touch for the fuller stage. Layered gates stand in front of the rest: inv-044 and inv-070 hit integer transforms, inv-059 hits quarter-pixel viewBox mapping, inv-050 hits an unimplemented child element, inv-086 hits raster work limits at default size, inv-089 hits use references.
Decision: the roles-plus-unsigned-area engine is already correct for single-contour nonzero crossings (the contour role is unconditionally plus one, so unsigned union equals true nonzero fill including the crossing lens), so implement that narrow admission as R34 increment one: graduate the bowtie and self-intersecting rejection pins to admission tests, keep touching-hole, coincident-spike, evenodd and multi-contour rejection pins intact, and triple inv-058 for the record. The fuller spatial stage stays queued for inv-067 and the simplicity trio plus inv-062 at 3.75x magnification. Next: land the narrow rule with its tests and triples under a full gate.

## Narrow single-contour admission refuted by bowtie inversion (2026-09-27 spike analysis)
Question: does admitting single-contour ProperCrossing under nonzero fill through the existing roles-plus-unsigned-area engine paint the true nonzero union, as the preceding spike analysis claims?
Method: implemented the narrow rule for real (AdmitsCrossingContours at the three ClassifySet gates: admit exactly single-contour ProperCrossing under nonzero), graduated the eight flipped pins to admission tests with analytic expectations, and ran them in Release.
Facts: the 8x8 integer bowtie 0,0 8,8 8,0 0,8 renders essentially empty (71-byte PNG, all pixels below half coverage) instead of the analytic 32-area union, and the fractional bowtie leaves its lobe interiors transparent. Root cause: FanSignedArea triangulates each contour from its first vertex with SIGNED accumulation, so the two congruent opposite-signed fan triangles of a symmetric bowtie cancel within the contour before the roles-stage absolute value ever applies; roles multiply per-contour unsigned areas but cannot repair intra-contour cancellation. The preceding analysis claim that the contour role is unconditionally plus one, while true, is irrelevant to this failure mode. Consequence: the inv-058 spike residual (3997 px) cannot be attributed to edge coverage alone and the narrow rule is unsound.
Decision: the narrow admission is refuted and fully reverted (product code, all eight test graduations, scratch renders); the 67 gate-pinning tests pass again. ClassifySet stays fully gated. True R34 admission needs per-pixel spatial winding (active edges) or non-overlapping triangulation, because no contour-level role can fix intra-contour signed cancellation. Next: design the spatial stage against the bowtie (must paint 32-area union, lens included) as the first acceptance case, keeping the layered-gate map from the preceding analysis.


## Exact halves probe confirms output size, opens fringe coverage (2026-09-27 decision analysis)
Question: does the pinned oracle confirm ties-away-from-zero output sizing on exact .5 root dimensions, and what does its content mapping look like?
Method: minimal 10.5x6.5 root with a 10x6 red rect (artifacts/probe-tmp2/half.svg); our candidate renders 11x7 (80 bytes) with geometry at 1:1 user units; fresh triple pinned captures at an 11x7 window plus per-pixel RGBA grids of both sides.
Facts: triple is 3/3 byte-identical (SHA-256 58e16e5d5369788656b2bfe2774cc2094595e7025610acb780ff136547dba9fb); placement agrees exactly (10x6 opaque block at origin on both); the 17 differing pixels are exactly the fractional fringe (right column alpha 122, bottom row 118, corner 56 on the oracle versus fully transparent margins on ours), i.e. Chrome keeps a fractional 10.5x6.5 layout with analytic fringe coverage while we round the output to 11x7 and map geometry 1:1.
Decision: halves-mode output sizing stands compatible (11x7 contains the content on both sides with identical placement); fringe AA coverage of fractional remainders is a separate open S11-family gap, not a sizing refutation. Next: fractional fringe coverage only if an inventory document needs it.


## Bowtie union matches the oracle except at the crossing vertex (2026-09-27 decision analysis)
Question: does the admitted split-triangulation bowtie union match the pinned browser, and where exactly does the pinned AlphaSum 8168 come from?
Method: minimal 8x8 integer bowtie (artifacts/probe-tmp2/bow.svg, the closed axis-aligned integer path gate spelling); our candidate (107 bytes) against a fresh triple pinned capture; per-pixel RGBA grids of both sides.
Facts: triple is 3/3 byte-identical (SHA-256 2fa094a08f21acd6896a1071d2fb30edf7cb5954ebf8751ae12e060a4de41a44); 61 of 64 pixels are byte-identical. The only differences are the two crossing-corner pixels (3,4) and (4,4): ours alpha 128 each, oracle 144 each (16 steps, whole residual). Analytic single-lobe coverage there is 128, so we paint exactly one lobe while the oracle adds 16; our pinned sum 8168 is the exact analytic union with sixteen half-coverage pixels quantizing 127.5 up to 128 (the loops share only the crossing point, so nothing doubles).
Decision: split-triangulation union stands admitted with pinned behavior (8168 plus exact lobe pixels); the crossing-corner plus-16 is an oracle-side vertex coverage rule, an open S11 micro-gap with no new iteration and no test change.

## Diagonal dash chevron residual is edge noise, not a second cap extension (2026-09-27 decision analysis)
Question: is the small vertex-crossing chevron residual (dd11) a second dash cap-extension semantic like dd2, or edge coverage noise?
Method: re-probed the 16x16 diagonal chevron (dash 4 2, width 2) after the union-runs fix against the retained pinned capture; per-pixel RGBA grids of both sides.
Facts: 28 pixels differ with maximum alpha error 32 at row 2 column 4 (160 ours versus 192 oracle); binary coverage maps are identical on all 16 rows, so placement agrees everywhere and only coverage values differ by up to 32 steps scattered across edge pixels. The maximum channel error 255 is a faint-versus-transparent artifact (ours alpha 4 red against oracle transparent at row 2 column 7), not a color error.
Decision: edge-noise family with the straight-dash probes (no placement delta anywhere, so no cap-extension analog exists to fake or fix); no new iteration under the eliminated-hypothesis rule and no regression test, since nothing semantic distinguishes the outputs.


## Solid strokes drop half their outline on crossing centerlines (2026-09-27 decision analysis)
Question: why does a solid width-2 stroke on the 8x8 bowtie paint only the left half while the dashed twin paints both, and where should the fix go?
Method: solid and dashed bowtie probes with fill none (artifacts/probe-tmp2/solidbow.svg, dashbow.svg), a fill-plus-stroke probe (strokebow.svg) with a fresh pinned triple, per-pixel RGBA grids, and a temporary unionRuns flip for solid strokes (reverted); the dashed polygon-path twin is pinned by test.
Facts: solid stroke paints only x0-3 (left half, 108-byte candidate) while dashed paints both sides; flipping solid callers to unionRuns restores exact 180-degree symmetry, proving the outlines exist on both sides and first-vertex winding roles cancel the right half (the S12 dropped-run mechanism, deliberately scoped to dashes at the time). The fill-plus-stroke triple (3/3 byte-identical SHA-256 a27df58ff14b9af566f2e6a71b721a743df6013f46d980a0f051e1c8063245b7) shows 43 differing pixels with max channel 255 and max alpha 233: chrome paints stroke nearly everywhere while ours leaves the right lobe interior red (fill shows through the dropped stroke).
Decision: not fixed this turn. Blanket union would fill closed-stroke holes (square centers); open-only union is unsplittable because LowerStrokeContours mixes open and closed contours into one outline soup without provenance, and a polygon-only fix would break polygon-path twin consistency. The principled fix is union within orientation groups (same-sign overlap unions, opposite-sign holes kept), which generalizes the S12 dash rule and preserves rings. Outcome 2026-09-28: landed role-zero lift instead of grouped union (a non-degenerate zero role is provably always a same-wound island, so no grouping is needed): solid bowtie stroke paints symmetrically and the fill-plus-stroke residual collapses from 43px/255/233 to edge-level 27px/max-alpha-22 against the same triple; ring holes stay holes with the full gate green (commit 300fa1d with symmetry and coverage tests).

## Multi-crossing nonzero contours split iteratively under a pairwise loop guard (2026-09-28 decision analysis)
Question: can the R34 single-crossing split generalize to multi-crossing contours without reopening the batch-90 unsoundness that withdrew inv-058?
Method: TrySplitSingleCrossing generalized to TrySplitCrossingContours behind a pending-range queue, shared by the solid, gradient and radial lowering paths; even-odd keeps the exact single-contour single-crossing gate; quarter-grid mapping upgrades to finite edges when split vertices leave the grid; the final loop set passes AreSplitLoopsAdmissible (edge contacts beyond bitwise-shared vertices reject, mutual containment rejects, nesting and disjoint siblings admit). Probe renders of a double-crossing polygon, a disjoint triangle-plus-bowtie pair and the even-odd twin, plus an independent Python exact-coverage check by triangle inclusion-exclusion.
Facts: the double-crossing polygon renders 4231 with exact tiling confirmed independently (pairwise loop overlaps 0.0, every fully covered pixel 255, every exterior pixel 0, edge pixels within half an alpha step of analytic coverage over 16.571 area); the disjoint pair adds exactly (8164 plus 8168 equals 16332); the even-odd twin still rejects with the simplicity message; touch, overlap and nesting guard units behave; tight-budget multi-crossing denies with full release; full gate 2467 core plus 100 visual plus 6 font green.
Decision: nonzero multi-crossing contours admitted through the iterative split with the guard; even-odd multi-crossing, touches, coincident overlaps and degenerate edges stay gated. Next: re-probe the inv-058 and inv-086 star family for graduation, then the inv-044 and inv-089 pentagrams; the inv-050, inv-067 and inv-070 trio still needs touch handling or the full spatial stage.


## Integer tokenizer misreads first-subpath relative moveto pairs as absolute (2026-09-28 decision analysis)
Question: why does the Turkey star probe render a triangle left of the true star while the same pentagram shape rejects everywhere else?
Method: white-box dump of the retained split loops for the inv-086 star subpath (temporary instrumented test, removed afterwards); traced the tokenized vertices against the path data; exact-arithmetic replay of the classification and greedy split for the Tunisia star; SVG-level bisection of the Turkey path (star-alone, arc-alone, star-plus-one-arc); granularity ladder at doubled output.
Facts: the retained loops carry mixed absolute and raw-delta vertices (41750,0 then 13568,-4408 then -8386,11541): ParsePath initializes the subpath moveto case to uppercase M, so a first-subpath lowercase m resolves its own position correctly by accident from the zero start but marks following implicit pairs absolute. All origin-start test paths masked the bug because absolute and relative coincide there. The splitter and guard then operate consistently on garbage and can admit it (the unsound-looking Turkey triangle); the true star is a clean pentagram whose greedy first split strands the remaining crossings across loops, so the pairwise guard rejects it. The Turkey star-plus-arc refusal is scale-invariant true topology at the combination level, and the Tunisia star refuses the same way by exact replay. Fractional paths are unaffected (their parser accumulates the moveto correctly, pinned by a new twin test).
Decision: capture the first command and seed the moveto case from it (three-line fix with relative, absolute-control and fractional twin tests; the integer twin fails without the fix and passes with it). No inventory graduation follows: inv-086 stays gated for its true pentagram, inv-058 stays gated the same way, and the full 92-document re-probe shows zero drift from the repair (only integer first-subpath lowercase-moveto implicit pairs change streams, and every in-corpus witness still rejects identically). Next: pentagrams and the simplicity trio need the full spatial stage, not greedy splitting.


## Linear gradients inherit stops, spread and units backward through radial href (2026-09-28 decision analysis)
Question: does the admitted radial-from-linear cross-type inheritance have a sound mirror, and what does Chrome do for linear-from-radial?
Method: stops fallback plus spread, units and reference-scale fallback in the linear resolvers with the vector staying linear-local; bare-href-only restriction for vector flags mirroring the partial-radial-vector gate; graduated the cross-type linear rejection pin to an admission test asserting inherited stops with a linear-local vector; twin renders plus fresh pinned Chrome captures of the crossed and local documents.
Facts: cross-type twin renders byte-identical to the same-type twin in our engine; pinned Chrome captures of both documents are byte-identical to each other; our cross-type candidate is byte-exact against Chrome (0 px). Partial vector overrides with a cross-type reference stay explicitly unsupported, and undefined ids keep the defined-reference message, so backward-only resolution keeps the reference graph acyclic exactly as in the landed direction. No in-corpus linear-href documents exist, so disposition impact is zero by construction with same-type behavior statement-identical. Full gate 2474 core plus 100 visual plus 6 font green.
Decision: backward linear-from-radial admitted with the vector staying linear-local; forward gradient href stays gated in both directions. Next: nothing further in the cross-type matrix until a corpus case needs forward references.


## Magnified use instances of flattened curves outrun the lowering tolerance (2026-09-28 decision analysis)
Question: does flattening tolerance survive instance magnification, and where exactly does the admitted map end?
Method: use-with-scale(8) curve twin vs direct render; unscaled curve use twin; straight-polygon use at 8x vs direct; reflected curve use vs hand-mirrored baseline; VisualDiff on all pairs.
Facts: unscaled curve use is byte-exact (0 px); reflected curve use is byte-exact; straight-polygon use at 8x is byte-exact because affine maps preserve straight edges; curve use at 8x differs by 128 px with maximum alpha error 111 across the curve band. Model: lowering-space tolerance is exact by construction since frames compose into the lowering map; root viewBox and outputScale flow through resolved output scales into ScaledFlatness (proven by the standing viewBox-direct twins); only post-lowering instance magnification escapes, scaling the subdivision error by exactly the instance scale factor.
Decision: gate magnified direct use instances of FlattenedCurves content (unit-or-smaller integer scales pass through; straight content renders exactly at any scale); mixed sets reject as a whole. Symbol viewBox maps and nested frames need the same treatment with double-scale care and stay queued. Per-instance re-flattening would need deferred definition lowering and stays architectural.


## Symbol viewBox maps of flattened curves gate on composed magnification (2026-09-28 decision analysis)
Question: does the instance-magnification gate extend to symbol viewBox maps, and where do unmagnified and straight symbol instances stand?
Method: symbol curve content at 8x viewBox map vs direct render; unit-map symbol curve twin vs direct; straight symbol polygon at 8x map vs direct; VisualDiff on all pairs.
Facts: magnified symbol curves would stretch the same lowering-time subdivision error (rejected before measuring); unit-map symbol curves render byte-identical (0 px); magnified straight symbol polygons render byte-identical (0 px) because affine maps preserve straight edges. Uniform viewBox scales compose multiplicatively with integer instance scales; unit-or-smaller composed maps pass through exactly.
Decision: gate magnified symbol instances of FlattenedCurves content across all four polygon paints with the instance scale folded in, mirroring the direct-use gate; straight content and unit maps render exactly. Nested frames still apply post-lowering magnification outside both gates and stay queued with per-instance re-flattening.


## Flattening tolerance accounts for every post-lowering magnification (2026-09-28 decision analysis)
Question: which magnification sources can still outrun the lowering-time subdivision tolerance, and is anything left unaccounted?
Method: audited every map applied after curve lowering against the tolerance derivation: lowering-space maps compose into the lowering transform by construction; root viewBox and outputScale flow through resolved output scales; direct integer instance scales gate above unit magnitude; symbol viewBox maps gate above unit composed magnification; use-site viewport frames gate through the pre-existing scaled-viewport use check on both the immediate and deferred resolution paths. Probed curve content through each path.
Facts: nested-viewport curve twins stay byte-exact; unscaled, reflected and straight-content instance twins stay byte-exact at any admitted scale; magnified curve instances reject on both the immediate and deferred paths; curve content in a magnified nested viewport rejects through the pre-existing frame gate. No inventory document reaches any of the new gates (verified by re-probe). Deferred definition lowering would be needed to admit magnified instances instead of rejecting them.
Decision: the tolerance-derivation thread of R35 closes here with two regression pins (frame gate and deferred-path gate); the remaining R35 work is per-instance re-flattening architecture and S11 exact-AA calibration, neither of which changes any admitted output today.


## Raster denial coverage grows to pattern polygons and solid curves (2026-09-28 decision analysis)
Question: which admitted raster paths still lack tight-budget denial coverage?
Method: surveyed RenderBudgetTests by geometry kind against the R39 grow-with-new-kinds instruction; mirrored the radial and gradient polygon denial shape for the two missing polygon-paint kinds.
Facts: pattern-polygon and solid-curved fills denied and released under the same tight raster budget on the first attempt with no product change; strokes share the polygon rasterizer and bands share the rectangle rasterizer, so separate denial twins there would exercise identical code paths. Full gate 2484 core plus 100 visual plus 6 font green.
Decision: denial coverage for the polygon rasterizer is complete across solid, gradient, radial and pattern paints plus curved variants; no new denial tooling was needed. Next: denial cases for genuinely new raster paths only.


## Root xml:space seeds whitespace inheritance for text descendants (2026-09-28 decision analysis)
Question: why does root-level xml:space validate but never affect rendering?
Method: traced the whitespace inheritance chain from SvgRootAttributes through container resolution to text lowering; mirrored the established text-level whitespace twin tests at the root with the triangle space font.
Facts: text elements resolve their own xml:space, and containers inherit through the chain, but the root seeded every chain from Default regardless of its own admitted xml:space attribute. Seeding the root base from the validated root mode preserves the full precedence (inline and stylesheet CSS first, element xml:space next, inherited seed last) with no other path touched. The seed test fails without the fix and passes with it; the CSS-override twin passes either way. Full gate 2486 core plus 100 visual plus 6 font green.
Decision: root xml:space now seeds descendant whitespace inheritance; container-level xml:space still rejects at attribute validation and stays queued behind its own allow-list entry.


## Container xml:space feeds whitespace inheritance like the root seed (2026-09-28 decision analysis)
Question: does group-level xml:space inherit to text descendants, and what rejects bad values where?
Method: extended the root-seed work into SvgTextInheritance.Resolve (element attribute slotted between merged CSS and the inherited mode) with the Group allow-list entry; mirrored the root twin tests at group level plus an invalid-value pin; fail-without-fix check by stashing the product change.
Facts: group preserve renders byte-identical to the text-level preserve twin; group inline CSS overrides group xml:space exactly like the root override; invalid group values fail as MalformedInput. The seed and override twins fail without the fix; the invalid pin passes either way because the XML layer rejects out-of-range xml:space values before product validation, which the pin now locks in. Text elements keep their own resolution path untouched. Full gate 2489 core plus 100 visual plus 6 font green.
Decision: xml:space inheritance is complete across root, containers and text elements with CSS-first precedence throughout; no PLAN.md status line (no finding owns this micro-thread; the design record carries both whitespace entries).


## One clip and paint definition binds per referencing object box (2026-09-28 decision analysis)
Question: does any definition state leak across instances sharing one clip-path or paint-server id?
Method: two rects of different sizes sharing one objectBoundingBox clipPath and one objectBoundingBox linearGradient; per-pixel maps plus exact AlphaSum analysis; white-box retained-geometry dumps; swapped, three-rectangle and canvas-size bisection probes.
Facts: both twins bind per instance (clip halves (0,0,2,8) plus (8,0,4,8); ramps restart per box with the wide left edge red-dominant). An early scare about a narrowed second clip traced to an unverified probe file, not the product: byte-verified re-runs render exactly, and no test had ever referenced one clip id twice. Full gate 2491 core plus 100 visual plus 6 font green.
Decision: per-instance clip and paint binding stands pinned by twin tests (structural pixels plus exact sums 12240 and 24480); probe inputs must be byte-verified before drawing conclusions since ignored scratch is shared with concurrent work. Next: nothing open on this route.


## Boundary locks for recent landings need no product change (2026-09-28 decision analysis)
Question: which recently landed boundaries lack regression pins?
Method: audited the tokenizer, cross-type href and multi-crossing split boundaries for untested-but-behaving cases; added twin and denial pins only.
Facts: second-subpath lowercase moveto with implicit pairs renders byte-identical to its absolute twin; forward cross-type gradient href rejects with the defined-reference message; gradient and radial multi-crossing contours reject under even-odd fill exactly like the solid path. All four pass without touching product code. Full gate 2495 core plus 100 visual plus 6 font green.
Decision: boundaries locked; no new denial tooling or product change. Next: keep growing pins alongside each landing rather than in arrears.


## Fractional parser resets the current point on closepath (2026-09-28 decision analysis)
Question: why does a relative moveto after closepath paint in the wrong place through fractional paths while the integer twin is exact?
Method: relative moveto after closepath vs absolute twin at the spec-mandated subpath start; white-box retained-loop dump; fail-without-fix stash check; full 92-document re-probe with oracle re-verification for the moved document.
Facts: pre-fix the moveto accumulated from the last point (the twin differs by 8 px with max alpha error 255); the integer tokenizer already reset on closepath, so only the fractional path parser needed the three-line subpath-start tracking. Post-fix twins are byte-exact. Blast radius: full gate green (2496 core plus 100 visual plus 6 font, including the manifest entries) with zero failures; inventory shows exactly two drifts, both improvements or advancements: inv-067 flame doors relocate to spec positions (residual collapses from 25870/179 to 2510/63 with zero alpha error, triple re-verified 3/3 byte-identical) and inv-050 advances past polygon simplicity to the non-root stylesheet gate with all octagon and letter contours parsing.
Decision: closepath now resets the current point in both path parsers; the batch-100 entries record the two drifts. Next: nothing open on moveto handling.

## Fractional geometry transforms route per site, not through AxisTransform conversion (2026-09-28 decision analysis)
Question: how should the twelve transform-gated inventory documents admit fractional container and geometry transforms, by converting AxisTransform to double or by extending a per-site precedent?
Method: consumer census of AxisTransform across src (70 sites in 14 files, including SvgReferenceResolver and SvgDefinitionParser currently in R35 and S13 flight); read the admitted fractional gradientTransform path (SvgGradientTransform with its own parser); stack attribution of the twelve gated documents (six container-inheritance translate and scale sites, six leaf-resolver sites, all pre-map parse gates already proven scale-independent by batch-105).
Facts: AxisTransform carries int scale and translation with checked integer overflow gates through rectangle, line, vertex, gradient-point, clip-box and band lowering; converting it would touch every site plus the exactness contracts that keep integer vertices exact. Fractional gradientTransform forms bypass it entirely through a dedicated double parser evaluated through recorded inverse paint maps, a route geometry cannot reuse because outlines need forward application at every lowering site. The twelve gated documents therefore wait on per-site double handling at container and leaf resolution, not on a type conversion.
Decision: no AxisTransform conversion in this slice; AxisTransform stays the calibrated integer subset. When R37 opens, the first per-site target follows the gradient precedent with its own parser and map composition, twin-gated per paint path. Next: R37 owner selects that target; the twelve stack attributions in batch-105 staging carry the case list.

## Fractional path-outline widths miss the oracle in two residual families (2026-09-28 decision analysis)
Question: does the double-ready outline kernel agree with Chrome at fractional stroke widths, which would admit inv-064, inv-065 and inv-082?
Method: temporary local parse-only widening of ParseStrokeWidth, SvgAdmittedStroke.Width and the LowerStroke path to double (reverted after the experiment); six 16x16 integer-coordinate research probes at widths 0.5, 1.5, 2.5 and 3.5 across butt, diagonal, round-cap, round-join and closed outlines; fresh 16x16 oracle triples 3/3 identical; VisualDiff against the staged references.
Facts: 0 of 6 exact. Family A is edge-quantum rounding at max error 1 (narrow 24 pixels across rows 7 to 8; closed loop 64 edge pixels). Family B is geometry at errors 14 to 86 (orthogonal crossing 4 pixels in bbox 6 to 9 at max error 16, the overlap double-compositing signature; diagonal 26 pixels at 14; round caps 12 pixels at the tips at 23; round join 56 pixels at 86). The plumbing compiled with exactly one touched local and rendered all six, so readiness is confirmed while agreement is refuted.
Decision: the widening is withdrawn and fractional outline widths stay gated; the six probes stay in the research manifest as UnsupportedFeature ratchets. Next: S11 owns family A as coverage-to-alpha quantum; caps, joins and overlap compositing each need their own hypothesis before any outline-width admission; odd-integer widths stay gated untested separately.

## Overlap compositing is exact orthogonal and residual on diagonals (2026-09-28 decision analysis)
Question: is the batch-108 orthogonal-crossing residual a general compositing flaw or fractional-specific?
Method: three admitted-syntax 16x16 probes (even-width orthogonal cross, overlapping opaque rect pair, even-width diagonal X); default-option render with no product change; fresh 16x16 oracle triples 3/3 identical; VisualDiff.
Facts: orthogonal cross 0 pixels and rect pair 0 pixels, so painter-algorithm overlap compositing agrees exactly on axis-aligned geometry; diagonal X carries 87 pixels at max error 31 in bbox 1 to 14, ratcheted with those numbers in the research manifest. The batch-108 4-pixel crossing residual is therefore fractional-overlap-specific, while diagonal strokes carry the standing diagonal-edge delta with no fractionality involved.
Decision: no product change; the X probe stays as an admittedRendering ratchet and the two exact probes leave no manifest entry. Next: the fractional-crossing residual belongs to any future outline-width hypothesis; the diagonal delta stays the S11 diagonal item.

## Odd-integer outline widths miss the oracle like fractional ones (2026-09-28 decision analysis)
Question: do odd-integer stroke widths agree with Chrome where fractional widths did not, which would advance inv-009, inv-028, inv-032 and inv-041?
Method: temporary local removal of the evenness check in ParseStrokeWidth (reverted after the experiment); six 16x16 integer-coordinate research probes at widths 1 and 3 across butt, diagonal, round-cap, round-join, hairline and closed outlines; fresh 16x16 oracle triples 3/3 identical; VisualDiff.
Facts: 0 of 6 exact with the same two families as fractional widths. Edge-quantum rounding at max error 1 (hairline 12 pixels on row 7 only; closed loop 8 pixels). Geometry at errors up to 106 (orthogonal crossing 4 pixels at 63 against 16 fractional; diagonal 54 pixels at 10; round caps 16 pixels at the tips at 20; round join 62 pixels at 106). Straight hairline edges otherwise agree, so the quantum gap is one-sided single-row rounding plus caps, joins, crossings and diagonals.
Decision: the evenness removal is withdrawn and odd widths stay gated; the six probes stay in the research manifest as UnsupportedFeature ratchets. Next: inv-009, inv-028, inv-032 and inv-041 wait on the same S11 quantum and cap, join, crossing and diagonal hypotheses as fractional widths; no separate odd-width admission exists.

## Rect coverage agrees at half edges and misses at quarter-left edges (2026-09-28 decision analysis)
Question: does the edge-quantum residual live in the coverage kernel or the outline path, and what rounding rule explains it?
Method: two admitted-syntax 16x16 black rect fills (x 10.5 and x 10.25, width 5) with no product change; default render; fresh triples 3/3 identical; per-pixel sampling of oracle against candidate.
Facts: half edges exact at 0 pixels with 128 against 128 on both half-covered columns. Quarter edges miss 12 pixels at max error 1 on column 10 only: nominal 0.75 coverage renders 191 here against 192 in Chrome, while nominal 0.25 coverage agrees at 64 and full coverage at 255. No rounding rule maps input 191.25 to 192, so Chrome effective coverage there is not 0.75; uniform edge expansion is refuted by bounds (needs plus 0.0029 left but at most 0.0010 right). The divergence is positional or coverage-formula, not output rounding, and the outline Family A residuals share its one-level signature.
Decision: the quarter-edge probe stays as an admittedRendering ratchet of 12 pixels at max error 1; the exact half-edge probe leaves no manifest entry. Next: S11 resolves against Skia sources which of edge position, coverage formula, or quantum bias explains the 0.75-only disagreement.

## Skia edge alpha truncates coverage times 256; ours rounds times 255 (2026-09-28 source analysis)
Question: what quantization rule explains 191 here against 192 in Chrome at nominal 0.75 coverage, with agreement at 0.25, 0.5 and 1.0?
Method: DEPS revalidation against the browser pin (cr-rev maps Chromium 1681091 to main commit 86cee6df; its DEPS pins skia 8b8c3872, which the retained f8b66b7 record does not match); blobless sparse checkout of that revision, read-only under external/skia and unreferenced; read the SkScan_AAAPath trapezoid alpha functions, the SkBlitter fat-rect route, the SkDraw rect routing, and our WithCoverage.
Facts: Skia trapezoid_to_alpha truncates SkFixed area right by 8 (floor of coverage times 256, full coverage special-cased to 255 through fixed_to_alpha) while ours rounds Alpha times coverage half-away (times 255). Predictions fit all four sampled anchors: 0.75 gives 192 against 191, 0.25 gives 64 against 64, 0.5 gives 128 against 128, 1.0 gives 255 against 255. The blitFatAntiRect scalar route would give 191 and is excluded by the oracle; filled AA rects route through SkScan AntiFillRect into the analytic mask machinery. Integer-edge exactness is rule-invariant since both rules agree at 0 and 1, so a switch only moves fractional-coverage pixels.
Decision: the quantum rule is solved as specified; applying floor256 with the 255 special case is an S11 calibration change (hot rasterizer plus full re-probe plus manifest updates) for its owner, expected low-risk by the invariance argument and verified by re-probe, not by assertion. Next: reconcile the retained DEPS record against 8b8c3872; family-B geometry is untouched by this rule.

## Quantum rule holds on vertical edges with transposed signature (2026-09-28 decision analysis)
Question: does the floor256 rule generalize to horizontal fractional edges, which flow through Skia partial-height row machinery instead of trapezoid columns?
Method: admitted-syntax 16x16 black rect at y 10.25 height 5 with no product change; default render; fresh triple 3/3 identical; per-pixel sampling of oracle against candidate.
Facts: 12 pixels at max error 1 on row 10 only, the exact transpose of the vertical case. Top edge at nominal 0.75 coverage gives 192 against 191; bottom edge at 0.25 agrees at 64; full rows at 255. The floor256 rule therefore covers both axes with one asymmetric anchor each, and the outline Family A residuals share the signature in both orientations.
Decision: the vertical probe stays as an admittedRendering ratchet of 12 pixels at max error 1. Next: the S11 switch now has two-axis evidence; no further axis to check before applying it behind twins and a full re-probe.

## Diagonal coverage differs by Skia fixed-point construction, not precision (2026-09-28 source analysis)
Question: why do integer-coordinate diagonal edges miss by errors 14 to 31 when the same analytic machinery is exact on axis geometry?
Method: read the SkScan_AAAPath row fillers at the pinned Skia revision (compute_alpha_above_line, compute_alpha_below_line, partial_triangle_to_alpha, approximate_intersection); the diagonal-X probe keeps integer vertices interior to the clip so edge positions are exact in both systems.
Facts: every stage truncates in fixed point (triangle alphas shifted right by 9, stores shifted right by 8 with 0xFF masking, saturated accumulation carrying a dY halved bias, triple-shifted triangle approximation with its own bug ticket) and multi-edge pixels average intersections coarsely by midpoint. Exact analytic area therefore cannot agree with this walk: the residual is structural, and integer vertices rule out position quantization as the source.
Decision: diagonal exactness is unreachable by improving analytic precision; it needs bug-compatible replication of the Skia fixed-point walk or a negotiated fidelity target, for S11. Next: no further diagonal probes until one of those two routes opens; the X ratchet pins the current delta.

## Even-width round caps and joins carry residuals on admitted syntax (2026-09-28 decision analysis)
Question: do round caps and joins agree with Chrome at admitted even widths, closing the last calibration gap in the stroke map?
Method: two admitted-syntax 16x16 probes (width 4 round-cap bar, width 4 round-join chevron) with no product change; default render; fresh triples 3/3 identical; VisualDiff.
Facts: round caps miss 12 pixels at max error 24 confined to the tip zones with straight edges agreeing; round joins miss 56 pixels at max error 201. Extents match the fractional-width cap and join probes pixel for pixel while magnitudes scale with width, so the arc-fan tessellation disagrees at the same sites regardless of width class.
Decision: both probes stay as admittedRendering ratchets with those numbers. Next: cap and join agreement needs its own tessellation hypothesis; the stroke calibration map is now complete (quantum rule, overlap exact orthogonal, diagonals structural, caps and joins residual).

## Dash phase, offset and multisubpath agreement is exact on straight butt geometry (2026-09-28 decision analysis)
Question: does the admitted dash kernel agree with Chrome, and where does the inv-010 dash-edge residual come from?
Method: three admitted-syntax 16x16 probes (dasharray 4 2 plain, dashoffset 3, two-subpath dash phase) with no product change; default render; fresh triples 3/3 identical; VisualDiff.
Facts: all three byte-exact, so dash pattern, offset and multisubpath phase agree on straight butt geometry. The inv-010 residual therefore lies outside straight-butt dashing: that file pairs default fills with wide stroked multi-subpath outlines, pointing at fill winding or band topology rather than dash phase.
Decision: no probes retained since exact renders have no manifest home, and no product change. Next: re-localize the inv-010 residual before any dash-kernel work; none is indicated by these probes.

## Round caps and joins need conics through the flattener, not arc fans (2026-09-28 source analysis)
Question: what representation gap explains the width-scaled cap and join residuals pinned in the research manifest?
Method: read the Skia stroker cap and join builders at the pinned revision (SkStrokerPriv RoundCapper, RoundJoiner, MiterJoiner) against our SvgStrokeOutline arc fans.
Facts: Skia builds round caps and joins from exact conics with Root2Over2 weights, later flattened by the shared curve tolerance; ours emits polygonal arc fans with a segment-count rule. Fan chords therefore deviate proportionally to radius, which matches the observed width-scaled same-site residuals (12 pixels at 24 for width 4 caps, 56 at 201 for joins). Skia miter joins add an upright-right-angle fast path plus sine-half-angle limit math where ours miter within the caller limit with bevel fallback.
Decision: cap and join agreement routes through emitting conics into the existing curve flattener instead of fans, for S12 with the two even-width ratchets as acceptance. Next: no product change in this slice; miter agreement stays unprobed separately.

## Even-width miter joins miss at edge level along the spike (2026-09-28 decision analysis)
Question: does miter agreement hold where round joins already miss, completing the join map?
Method: admitted-syntax 16x16 obtuse chevron at width 4 miter with no product change; default render; fresh triple 3/3 identical; per-pixel profile of oracle against candidate.
Facts: 54 pixels at max error 13 spread along both spike edges and the inner notch across all rows (tip 43 against 48, inner 131 against 139) with flanks agreeing; the spike outline is subpixel-displaced, consistent with miter-limit math differences (Skia sine-half-angle computation with its upright-right-angle fast path) rather than tessellation or quantum effects.
Decision: the probe stays as an admittedRendering ratchet of 54 pixels at max error 13. Next: miter-limit math is the remaining join hypothesis; the join map is now complete with round residual, miter edge-level and bevel gated on paths.

## Square caps agree exactly, closing the cap map (2026-09-28 decision analysis)
Question: do square caps agree where round caps miss, completing cap coverage?
Method: two admitted-syntax 16x16 probes (width 4 square-cap bar, width 4 square-cap dash runs) with no product change; default render; fresh triples 3/3 identical; VisualDiff.
Facts: both byte-exact, including dash-run tip extensions. Square tips extend analytically with no arcs, unlike round caps which fan chords against Skia flattened conics. The cap map is now butt exact, square exact and round residual.
Decision: no probes retained since exact renders have no manifest home, and no product change. Next: nothing open on caps; joins remain round residual, miter edge-level and bevel gated.

## Translucent coverage agrees; floor256 model gains acceptance vectors (2026-09-28 decision analysis)
Question: does the floor256 rule survive translucent paint, where mask and blend quantizations compose?
Method: two admitted-syntax 16x16 probes (paint alpha 128 control landing on integers, paint alpha 127 stress) at quarter edges with no product change; default render; fresh triples 3/3 identical; per-pixel sampling.
Facts: both byte-exact. The 128 probe lands integers everywhere and does not discriminate. The 127 probe gives 95 at 0.75 coverage, 32 at 0.25 and 127 full on both systems, matching our round-half-away rule exactly; Skia mask-plus-blend arithmetic admits several integer formulas fitting these, so the blend constant is not isolated, but no isolation is needed since the systems agree. The S11 switch therefore carries eight acceptance values: opaque 192, 64, 128 and 255 plus translucent-127 95, 32 and 127 at the same geometries.
Decision: no probes retained since exact renders have no manifest home, and no separate translucent calibration work. Next: the S11 owner replicates the mask-plus-blend pair outcome against those eight values behind twins and a full re-probe.

## Opaque gradient ramps agree; transparent stops miss one red level (2026-09-28 decision analysis)
Question: do gradient interpolation and dithering agree with Chrome, including transparent stops?
Method: two admitted-syntax 16x16 probes (opaque red-blue ramp, transparent-red to blue ramp) with no product change; default render; fresh triples 3/3 identical; per-pixel sampling.
Facts: the opaque ramp is byte-exact, so interpolation and dithering agree there. The transparent-stop ramp misses exactly one pixel at (11, 8) with red 54 against 53 and identical blue and alpha, implicating transparent-stop premultiplied rounding rather than interpolation, dither or vector mapping.
Decision: the transparent-stop probe stays as an admittedRendering ratchet of 1 pixel at max error 1 with zero alpha error; the exact opaque probe leaves no manifest entry. Next: transparent-stop premultiplied rounding is the remaining gradient hypothesis.

## Radial paint adds zero error over solid disc edges (2026-09-28 decision analysis)
Question: does radial-gradient evaluation contribute its own residual on top of disc-outline geometry?
Method: two admitted-syntax 16x16 probes sharing one r=6 disc (solid black, white-to-black radial) with no product change; default render; fresh triples 3/3 identical; VisualDiff plus exact differing-pixel-set comparison.
Facts: both miss the identical 44-pixel edge ring at max error 61 in bbox 2 to 13, verified as the same pixel set, so radial evaluation adds zero error pixels and the entire residual is sampled-circle outline geometry. This matches the shared paint-box repaint claims and directs disc-family inventory residuals (Palau, Ukraine, Niger) at outlines, not paints.
Decision: both probes stay as admittedRendering ratchets with those numbers. Next: disc-outline sampling is the remaining disc hypothesis; no radial-paint work is indicated.

## Multistop opaque ramps agree; transparent middle misses two dither cells (2026-09-28 decision analysis)
Question: does multi-stop segment interpolation agree, and where does transparent-middle evaluation diverge?
Method: two admitted-syntax 16x16 probes (opaque red-green-blue ramp, transparent-green middle ramp) with no product change; default render; fresh triples 3/3 identical; per-pixel sampling.
Facts: the opaque ramp is byte-exact, so segment interpolation and dithering agree there. The transparent-middle ramp misses exactly two pixels at (4,4) and (4,12) with green differing and identical alpha, sharing one 8x8 Bayer cell while same-cell same-value (4,8) agrees; consistent with a dither-matrix difference confined to the transparent evaluation path, with opaque paths agreeing everywhere.
Decision: the transparent-middle probe stays as an admittedRendering ratchet of 2 pixels at max error 2 with zero alpha error; the exact opaque probe leaves no manifest entry. Next: transparent-path dither matrix for the paint owner; no interpolation work is indicated.

## Radial on rects misses one gray level; userSpace vectors agree (2026-09-28 decision analysis)
Question: does radial evaluation or userSpace vector mapping contribute residuals beyond geometry?
Method: two admitted-syntax 16x16 probes (bbox radial on rect, userSpace linear on rect) with no product change; default render; fresh triples 3/3 identical; per-pixel sampling.
Facts: userSpace linear is byte-exact, so vector mapping agrees. Radial on rects misses one pixel at (9, 13) with gray 13 against 12 and opaque alpha, implicating radial radius evaluation (two-circle cone math) rather than paint interpolation, which the disc twin already exonerated.
Decision: the radial-rect probe stays as an admittedRendering ratchet of 1 pixel at max error 1 with zero alpha error; the exact userspace probe leaves no manifest entry. Next: radial radius evaluation for the paint owner; vector mapping needs nothing.

## Ellipse and rounded-rect corners carry tessellation residuals (2026-09-28 decision analysis)
Question: do ellipse and rounded-rectangle outlines agree, completing primitive coverage?
Method: two admitted-syntax 16x16 probes (rx 6 ry 4 ellipse, 10x10 rect with rx 3) with no product change; default render; fresh triples 3/3 identical; VisualDiff.
Facts: ellipse misses 36 pixels at max error 35 on the curved flanks, same family as sampled-circle disc edges; rounded rect misses 20 pixels at max error 70 confined to the four corners, same family as round caps and joins. Straight edges agree in both.
Decision: both probes stay as admittedRendering ratchets with those numbers. Next: corners join the conic-flattening hypothesis; ellipse sampling joins the disc-outline item; primitive coverage is now complete with every curved family residual and every straight family exact.

## Quantum rule persists on opaque backgrounds with truncated compositing (2026-09-28 decision analysis)
Question: does the edge-quantum divergence survive opaque-background compositing, which takes a different blend path?
Method: admitted-syntax 16x16 black rect at x 10.25 on explicit white background with no product change; default render with white option; fresh white triple 3/3 identical; per-pixel sampling.
Facts: 12 pixels at max error 1 on column 10 with alpha clean: 63 here against 64 in Chrome at nominal 0.75 coverage over white (255 times 0.25 equals 63.75). We round half away while Chrome truncates, the same direction as the transparent quantum rule, now through the retained-destination compositing path. The floor256 switch must therefore align both WithCoverage and the opaque blend roundings.
Decision: the probe stays as an admittedRendering ratchet of 12 pixels at max error 1 with zero alpha error. Next: S11 owns both quantization points as one switch with the eight plus one acceptance values.

## Integer arcs carry curve-family residuals with a clean diameter (2026-09-28 decision analysis)
Question: does elliptical-arc conversion agree independently of cubic flattening?
Method: admitted-syntax 16x16 integer half-disc arc fill with no product change; default render; fresh triple 3/3 identical; VisualDiff.
Facts: 22 pixels at max error 61 confined to the curved half with the straight diameter edge agreeing, the same magnitude family as sampled-circle disc edges. Arc-to-segment conversion plus flattening therefore shares the curve residual shape with circles.
Decision: the probe stays as an admittedRendering ratchet of 22 pixels at max error 61. Next: arc conversion and circle sampling converge on one curve-flattening hypothesis for S11; the legacy cubic-arch probe covers cubics separately.

## Diagonal strokes on opaque white compress but keep residuals (2026-09-28 decision analysis)
Question: does opaque-background compositing damp the diagonal-coverage residual or interact with it?
Method: admitted-syntax 16x16 even-width diagonal stroke on explicit white with no product change; default render with white option; fresh white triple 3/3 identical; VisualDiff.
Facts: 50 pixels at max error 8 with zero alpha error, against 87 pixels at 31 on transparency. Compositing over white compresses the coverage residual into gray levels without removing it, confirming the residual is coverage-driven with compositing as a damper rather than a second source.
Decision: the probe stays as an admittedRendering ratchet of 50 pixels at max error 8 with zero alpha error. Next: diagonal work stays the S11 structural item; background interaction needs nothing separate.

## Container transforms compose as AxisTransform at inheritance (2026-09-28 decision analysis)
Question: is there a per-site shortcut for fractional container transforms below the AxisTransform conversion?
Method: read SvgContainerInheritance.Resolve and the tiger gate stack (fractional transform through container inheritance, thirteenth transform-gated document).
Facts: containers compose ParseTransform results directly into inheritedPresentation.Transform as AxisTransform before any leaf is visited, so fractional container args cannot sidestep the integer type at a lower layer; the 70-site conversion count stands and per-site targets must either change the inheritance carrier or resolve at leaf device maps.
Decision: no inheritance-level shortcut exists; the R37 per-site direction from the earlier entry constrains to leaf device-map resolution or a carrier change, both owned by R37. Next: nothing further to scope from outside that work.

## Miter math exonerated by axis rectangle; chevron residual is diagonal coverage (2026-09-28 decision analysis)
Question: is the chevron miter residual miter-limit math or diagonal edges?
Method: admitted-syntax 16x16 closed rectangular path stroke with four 90 degree miter joins on axis edges and no product change; default render; fresh triple 3/3 identical; VisualDiff. The EmitJoin ratio was also checked against the SVG miter-diagonal over stroke-width definition during scoping and matches.
Facts: 0 pixels exact. Miter math including ratio, limit, bevel fallback and inner and outer chains agrees; the chevron 54-pixel residual therefore lives entirely in its diagonal spike flanks and notch, which is the structural diagonal item, not a second miter mechanism.
Decision: axis-miter agreement recorded; the chevron ratchet stays with reattributed mechanism. Next: nothing open on miter math.

## Bevel on rounded outlines misses at corners with the arc family (2026-09-28 decision analysis)
Question: do admitted bevel joins agree where path bevel stays gated?
Method: admitted-syntax 16x16 stroked rounded rect with bevel joins and no product change; default render; fresh triple 3/3 identical; VisualDiff.
Facts: 56 pixels at max error 80 confined to the four corners, against 20 pixels at 70 for the solid rounded fill at the same corners. Bevel cuts are analytic flat segments with no tessellation of their own, so attribution favors the corner arcs shared with the solid case; bevel-specific isolation is impossible here since path bevel stays gated.
Decision: the probe stays as an admittedRendering ratchet of 56 pixels at max error 80. Next: corners join the conic-flattening hypothesis with caps, joins and rounded fills; bevel cuts need nothing separate.

## Miter fallback threshold differs by exactly two from Chrome (2026-09-28 source analysis)
Question: why does an acute spike miter here while Chrome bevels it, when obtuse miters agree?
Method: admitted-syntax 16x16 acute spike probe with default miter limit 4; row-profile of oracle against candidate; read Skia MiterJoiner and stroker limit plumbing at the pinned revision against our EmitJoin ratio.
Facts: the probe misses 50 opaque pixels with our spike taller by two rows. Algebra proves our ratio equals one over twice Skia sine-half-angle, so Skia bevels exactly when our ratio exceeds half the limit while we bevel at the full limit; with limit 4 the bands are both-miter below 2, split between 2 and 4 with our probe at 2.47, and both-bevel above 4. Skia passes one over the limit with no factor of two, so the two-times gap is in the comparison itself.
Decision: the probe stays as an admittedRendering ratchet of 50 opaque pixels. Next: S12 matches Chrome by comparing twice our ratio, or equivalently half the limit, behind twins; miter math itself stays exonerated.

## Non-scaling strokes under nonuniform scale agree exactly (2026-09-28 decision analysis)
Question: does vector-effect non-scaling-stroke composition agree through nonuniform container transforms?
Method: admitted-syntax 16x16 closed path with width 2 non-scaling stroke inside a scale(2,1) group with no product change; default render; fresh triple 3/3 identical; VisualDiff.
Facts: byte-exact. Transform and stroke-width handling compose correctly through the vector-effect path with fixed width under nonuniform scales.
Decision: no probe retained since the exact render has no manifest home, and no product change. Next: nothing open on non-scaling strokes.

## Transparent residuals grow where alpha is small: unpremultiply rounding (2026-09-28 decision analysis)
Question: is the transparent-stop residual interpolation rounding, dither position, or unpremultiply quantization?
Method: shifted transparent-stop ramp (rect moved plus 1 pixel, same bbox-relative gradient) with no product change; default render; fresh triple 3/3 identical; per-pixel sampling against the unshifted twin.
Facts: the shifted version misses 4 pixels against 1 unshifted, ruling out pure value rounding and pure dither cells. Edge pixels at alpha 11 show red 255 against 232 with alpha agreeing, consistent with premultiplied floor plus exact division magnified by the small divisor; mid pixels show red 75 against 73 at alpha 181. Residual magnitude grows as alpha shrinks, the unpremultiply signature.
Decision: the shifted probe stays as an admittedRendering ratchet of 4 pixels at max error 23 with zero alpha error. Next: transparent-stop work targets unpremultiply rounding, predicting error inversely proportional to alpha.

## Miterlimit 10 confirms the half-threshold rule on both branches (2026-09-28 decision analysis)
Question: does raising the limit to 10 reunite both sides on miter, as the half-threshold rule predicts?
Method: same acute spike with stroke-miterlimit 10 and no product change (our candidate bytes identical to the default-limit render); default render; fresh triple 3/3 identical; VisualDiff.
Facts: 46 pixels at max error 12 with no opaque blowout, against 50 opaque pixels at default limit. Both sides miter at limit 10 since code ratio 2.47 sits below both thresholds (4 here, 5 in Chrome); the remaining fringe is diagonal-flank coverage. The split-then-reunite pair confirms the threshold rule on both branches, not just the split.
Decision: the probe stays as an admittedRendering ratchet of 46 pixels at max error 12. Next: S12 applies half-limit comparison with two independent confirmations behind it.

## Both-bevel still misses along flanks; threshold fix is necessary but not sufficient (2026-09-28 decision analysis)
Question: when both sides bevel, does any residual remain beyond the branch choice?
Method: same acute spike with stroke-miterlimit 1 so both sides bevel, with no product change; default render; fresh triple 3/3 identical (oracle bytes identical to the default-limit capture, confirming Chrome bevels in both); row-profiled VisualDiff.
Facts: 46 pixels at max error 36 spread from the bevel cut down the long incoming flank, against 50 opaque pixels for the branch split. Fixing the threshold therefore removes the opaque blowout but leaves diagonal-flank coverage residuals of the structural kind.
Decision: the probe stays as an admittedRendering ratchet of 46 pixels at max error 36. Next: S12 applies the half-limit comparison first, then diagonal coverage; the two are now separately ratcheted.

## Polygon fractional edges miss boundary quanta through fan-triangulation noise (2026-09-28 decision analysis)
Question: why do fractional path and polygon edges miss where rect edges at the same coordinates agree?
Method: three admitted-syntax 16x16 probes (fractional orthogonal path, polygon and sealing polyline) with no product change; default render; fresh triples 3/3 identical; VisualDiff; read the polygon rasterizer quantization call site.
Facts: path and polygon miss 8 pixels at max error 1 on the half-edge column with byte-identical candidates and oracles, while the sealing polyline misses 4 pixels on its diagonal seal. The polygon rasterizer calls the same WithCoverage quantization as rects, so a shared input of exactly 0.5 would agree; the miss implies fan-triangulation and clip arithmetic deliver just under the boundary quantum where the rect fast path computes it exactly.
Decision: all three probes stay as admittedRendering ratchets with those numbers. Next: boundary-quantum robustness in fan area arithmetic for the coverage owner; the quantization rule itself is exonerated by the shared call site.

## Focal radial evaluation adds no new error class (2026-09-28 decision analysis)
Question: does focal-point radial evaluation diverge beyond the centered radial quantum?
Method: admitted-syntax 16x16 rect with focal offset inside the circle and no product change; default render; fresh triple 3/3 identical; per-pixel sampling.
Facts: 1 pixel at max error 1 with gray 61 against 60 and opaque alpha, the same single-quantum radius signature as the centered radial probe. Focal evaluation introduces no new error class.
Decision: the probe stays as an admittedRendering ratchet of 1 pixel at max error 1 with zero alpha error. Next: radial work stays the radius-evaluation item with two identical-signature ratchets.

## Quadratic conversion carries its own residual; arc rotation is inert on circles (2026-09-28 decision analysis)
Question: does quadratic conversion agree independently of cubics and arcs, and does x-axis-rotation change circular arcs?
Method: two admitted-syntax 16x16 probes (integer quadratic dome, circular arc with rotation 30) with no product change; default render; fresh triples 3/3 identical; VisualDiff; oracle-identity comparison across the rotation.
Facts: quadratic misses 16 pixels at max error 13 on the curve, its own conversion residual distinct from cubics and arcs. Rotated circular arc renders byte-identical candidates and oracles to the unrotated twin, so rotation parsing is end-to-end inert on circles as geometry requires; the rotated file leaves no independent probe.
Decision: the quadratic probe stays as an admittedRendering ratchet of 16 pixels at max error 13. Next: quadratic conversion joins the curve-flattening hypothesis with its own numbers.

## Elliptical arcs carry smaller flattening residuals than circular ones (2026-09-28 decision analysis)
Question: does distinct-radii arc conversion with radii correction agree independently of circular arcs?
Method: admitted-syntax 16x16 half-ellipse arc fill with no product change; default render; fresh triple 3/3 identical; VisualDiff.
Facts: 18 pixels at max error 22 on the flatter curve, against 22 at 61 for the semicircle; residual magnitude scales with curvature as flattening tolerance predicts, and radii correction places the shape correctly.
Decision: the probe stays as an admittedRendering ratchet of 18 pixels at max error 22. Next: arc conversion is covered across circular, rotated, elliptical and cubic inputs with one shared flattening hypothesis.

## Stroked-circle rings carry disc-outline residuals on both circumferences (2026-09-28 decision analysis)
Question: does opposite-wound ring coverage agree independently of disc fills?
Method: admitted-syntax 16x16 stroked circle with no product change; default render; fresh triple 3/3 identical; VisualDiff.
Facts: 69 pixels at max error 50 across the ring band, consistent with sampled-outline residuals on two circumferences rather than any ring-composition effect. The ring machinery itself needs nothing separate.
Decision: the probe stays as an admittedRendering ratchet of 69 pixels at max error 50. Next: disc-outline sampling covers rings too; no ring-specific work is indicated.

## Dashed circles run mirrored against Chrome, proven by opposite offset rotation (2026-09-28 decision analysis)
Question: is the dashed-circle phase gap a start-point shift or a mirrored traversal?
Method: two admitted-syntax 16x16 dashed circles at dashoffset 0 and 2.5 with no product change; default renders; fresh triples 3/3 identical; angular run mapping per renderer at both offsets; VisualDiff.
Facts: offset 0 misses 94 opaque pixels and offset 2.5 misses 98. Raising dashoffset rotates our pattern by half a period one way and Chrome pattern by half a period the other way, so traversal directions are mirrored; equal-and-opposite rotation excludes a shared direction with opposite sign convention, which would agree at offset 0. The locus is sampling order or dash-walk order on closed loops.
Decision: both probes stay as admittedRendering ratchets with those numbers. Next: S12 flips one traversal order for closed loops and re-probes against these two oracles.

## Dash mirror localizes to circle sampling, not the dash kernel (2026-09-28 decision analysis)
Question: does the mirrored traversal affect closed path loops or only sampled circles?
Method: admitted-syntax 16x16 closed rectangular path with dashes and no product change; default render; fresh triple 3/3 identical; VisualDiff.
Facts: byte-exact. Closed-loop dash traversal agrees, so the mirror lives in circle and ellipse sampling start or direction, not the dash machinery; the fix tries a sampling-direction flip first with the two dashed-circle oracles as acceptance, adjusting start point only if phase remains.
Decision: no probe retained since the exact render has no manifest home, and no product change. Next: S12 flips sampling direction for closed loops against the two oracles.

## Gradient strokes agree on solid geometry; degenerate bbox paints black here and nothing in Chrome (2026-09-28 decision analysis)
Question: does gradient-stroke evaluation agree, and what happens on degenerate bounding boxes?
Method: two admitted-syntax 16x16 probes (gradient-stroked rect, gradient-stroked zero-height line) with no product change; default renders; fresh triples 3/3 identical; VisualDiff; pixel sampling.
Facts: the rect case is byte-exact, confirming zero added paint error independently. The line case has a zero-height object bounding box: Chrome renders nothing while ours paints opaque black bands, a divergence in degenerate-bounds paint binding, not interpolation.
Decision: no probes retained since one is exact and the other records a behavior gap rather than calibration; the first probe file was withdrawn as an invalid agreement test. Next: S13 admits degenerate-bounds handling that rejects or skips instead of painting black.

## Stroked rounded rect joins the arc family; join setting changes our output but not Chrome (2026-09-28 decision analysis)
Question: does the stroked rounded rect agree, and does join style matter on smooth corners?
Method: admitted-syntax 16x16 stroked rounded rect with default miter joins and no product change; default render; fresh triple 3/3 identical; VisualDiff; cross-check against the bevel twin oracle.
Facts: 48 pixels at max error 80 at the corners. The oracle is byte-identical to the bevel twin oracle, so joins never fire on smooth corners in Chrome; our candidates differ by join setting, implicating tessellation joints treated as vertices in our outline lowering.
Decision: the probe stays as an admittedRendering ratchet of 48 pixels at max error 80. Next: tessellated-corner join firing belongs to the S12 outline work with the conic route.

## Square-cap dash runs stay unextended and pathLength normalizes exactly (2026-09-28 decision analysis)
Question: do square caps extend dash runs, and does pathLength scaling agree?
Method: two admitted-syntax 16x16 probes (square-cap dashes, pathLength 24 over computed 12) with no product change; default render; fresh triples 3/3 identical; VisualDiff.
Facts: both byte-exact. Square caps do not extend dash runs on either side here, and pathLength halves the pattern identically. No dash-cap or normalization work is indicated.
Decision: no probes retained since exact renders have no manifest home, and no product change. Next: nothing open on dash caps or pathLength.

## Element opacity on gradients scatters quantum residuals (2026-09-28 decision analysis)
Question: does element opacity compose exactly over agreed gradient interpolation?
Method: admitted-syntax 16x16 opaque-ramp twin with fill-opacity 0.5 and no product change; default render; fresh triple 3/3 identical; per-channel sampling against the exact opaque twin.
Facts: 114 pixels at max error 1 with zero alpha error and mixed plus-minus-one signs across channels, while the opaque ramp is byte-exact. Opacity scaling shifts dithered values across rounding boundaries or rounds differently; opacity sweeps would separate dither order from rounding rule.
Decision: the probe stays as an admittedRendering ratchet of 114 pixels at max error 1 with zero alpha error. Next: opacity sweeps for the paint owner; interpolation itself stays exonerated.

## Opacity sweep shows residuals growing as opacity drops (2026-09-28 decision analysis)
Question: how do element-opacity composition residuals scale across opacity levels?
Method: admitted-syntax 16x16 opaque-ramp twins at fill-opacity 0.25 and 0.75 with no product change; default render; fresh triples 3/3 identical; VisualDiff against the 0.5 and 1.0 points.
Facts: 120 pixels at max error 3 for 0.25 and 66 at 1 for 0.75, both alpha-clean, against 114 at 1 for 0.5 and exact at 1.0. Residual count and magnitude grow as opacity drops while alpha stays exact, implicating opacity application into dithered values rather than interpolation or opacity parsing.
Decision: both probes stay as admittedRendering ratchets with those numbers. Next: opacity application order for the paint owner; the sweep is complete at four levels.

## Closed-loop dash seams diverge while traversal agrees (2026-09-28 decision analysis)
Question: is the dash mirror general to closed loops, or does offset only disturb seam-crossing runs?
Method: admitted-syntax 16x16 closed rectangular path with dashoffset 2.5 and no product change (offset-0 twin is byte-exact); default render; fresh triple 3/3 identical; VisualDiff with row profile.
Facts: 4 pixels at max error 127 with matching alpha at the corners, against 94 opaque pixels for mirrored circles. Traversal therefore agrees on explicit-start loops; only runs crossing the closure seam diverge, while implicit-start sampled circles mirror globally.
Decision: the probe stays as an admittedRendering ratchet of 4 pixels at max error 127 with matching alpha. Next: seam-crossing run merging for S12 alongside the sampling-direction flip; the two are now separate items.

## Dash cap and join interactions follow their base families (2026-09-28 decision analysis)
Question: do round caps and miter joins on dashed paths add new mechanisms beyond their solid twins?
Method: two admitted-syntax 16x16 probes (round-cap dashes, miter-join dashes) with no product change; default render; fresh triples 3/3 identical; VisualDiff against the solid twins.
Facts: round-cap dashes miss 14 pixels at the tips against 12 for solid round caps, same arc-fan family; miter-join dashes miss 52 at 27 across join and runs, consistent with diagonal coverage plus dash runs already proven exact separately. No new mechanism in either combination.
Decision: both probes stay as admittedRendering ratchets with those numbers. Next: dash combinations need nothing beyond their base families.

## Rect bands and userSpace gradient line strokes agree exactly (2026-09-28 decision analysis)
Question: do sharp-rectangle band strokes and userSpace gradient line strokes agree, confirming two matrix exactness claims?
Method: two admitted-syntax 16x16 probes (band stroked rect, userSpace red-blue stroked line) with no product change; default render; fresh triples 3/3 identical; VisualDiff.
Facts: both byte-exact. Band lowering with mitered band corners agrees, and userSpace gradient evaluation on lines adds zero error as claimed.
Decision: no probes retained since exact renders have no manifest home, and no product change. Next: nothing open on bands or gradient line strokes.

## Floor256 switch work order: six sites, contained blast radius (2026-09-28 decision analysis)
Question: what exactly does applying the floor256 quantum rule touch, and how big is the blast radius?
Method: census of coverage-to-alpha quantization sites across src with no product change.
Facts: six sites quantize coverage or alpha with round-half behavior: WithCoverage (SvgConverter, coverage times alpha rounded half away), BlendSourceOver transparent branch (plus 127 over 255 thrice), ComposeFractionalChannel retained branch (half-outputAlpha and 255-times-outputAlpha-minus-one biases), and three identical MultiplyAlpha copies adding 127 over 255 in SvgConverter, SvgPaintResolver and SvgReferenceResolver. Exact entries stay exact under the switch since both rules agree at coverages 0 and 1; only fractional-coverage pixels move, which are exactly the ratcheted residual sets plus gated rejections.
Decision: the switch is a sequenced S11 calibration change (quantization sites, twin tests per affected path, full re-probe, manifest and research ratchet regeneration) for its owner, not a solo turn; containment follows from integer-edge invariance. Next: no partial switch, which would mix rules across stages.

## Miter threshold now tests the full displacement ratio (2026-09-28 implementation)
Question: does EmitJoin compare the emitted miter tip consistently with the caller limit.
Method: algebra of the emitted tip (halfWidth times (n1+n2) over (1+dot)) against the tested ratio, checked against the release-tag Skia MiterJoiner inverse-limit comparison; new limit-2 branch regression on the (0,0) to (5,0) to (1,3) turn whose true ratio is sqrt(10).
Facts: the old comparison halved the displacement length, so any limit in [true/2, true) wrongly mitered; the new test bevels at limit 2 while limit 1 still bevels and limit 4 still miters, and the new test fails on the old code. A full 66-probe sweep after the fix moves only research-miter-fallback-acute-16x16, from 50 pixels at max error 255 to 46 at 36 with bbox x1..8 y1..13, a strict improvement on the default-limit acute witness. The core suite holds 2497 green with no exact-fixture change.
Decision: the branch repair is retained with its regression and updated ratchet. Remaining spike-edge residuals stay under diagonal coverage and bevel-cut work, not miter math. Next: R41 degenerate gradient strokes, then row 2.

## Degenerate objectBoundingBox gradient strokes paint nothing (2026-09-28 implementation)
Question: why the R41 horizontal gradient path stroke paints black while Chrome paints none.
Method: traced the stroke repaint box (centerline bounds, zero height) into gradient polygon evaluation, where division by the zero box height yields non-finite normalized coordinates that interpolate to black; pre/post MemoryProbe renders with a stash-reverted before check across a five-case matrix.
Facts: the pre-fix candidate differs from the post-fix one by exactly the 24 black pixels at x2..13 y7..8 recorded in the review; post-fix horizontal, vertical, radial and use-instance candidates have PNG alpha-sum 0, while the userSpaceOnUse control paints the 24-pixel band (alpha-sum 6120). The full gate holds 2502 core plus 100 visual plus 6 font green with no fixture or research manifest change.
Decision: the shared rasterizer guard is retained in both gradient polygon rasterizers with userSpace paints exempt; the line-spelling rejection boundary is unchanged and no parser-level box rewrite was needed. Next: row 2.

## Solid, gradient and radial fills share one contour preparation path (2026-09-28 implementation)
Question: can the three duplicated validate, classify, split and signs pipelines share one owned contour boundary without changing behavior.
Method: extracted PrepareSeparatedContours returning a SeparatedContours payload (points, ends, signs, mapping); FinishPolygonFill, FinishGradientPolygonFill and FinishRadialPolygonFill keep only their paint-specific Retain calls with identical operation, charging and cancellation order. Full solution gate plus memory-probe scenarios serve as the behavior-preservation evidence.
Facts: net minus 52 lines in SvgOrthogonalGeometryLowerer with zero warning or error; gate counts identical to row-1 completion at 2502 core plus 100 visual plus 6 font with no fixture or research movement; all memory scenarios pass. FinishStrokeFill stays separate since it lifts zero roles without structure splitting, and reserve-before-allocate transfer reservations stay open.
Decision: the shared contour boundary is retained as the first row-2 slice. Next: transfer-boundary reservations with ownership, then raster-loop sharing through the same payload.

## Polygon retention reserves scene storage before allocating copies (2026-09-28 implementation)
Question: can the shared contour boundary reserve its retained arrays before allocating them, with ownership transferring into the fill.
Method: PrepareSeparatedContours now pre-reserves exact Scene bytes for points, ends and signs, re-reserves exact bytes after crossing splits while the split lists are still held, and carries the reservation in SeparatedContours into Create, which adopts it instead of reserving again; all other Create callers pass null and keep current behavior. Operation, charging and cancellation order are unchanged. Three pre-reserve denial tests, one exact Scene-bytes transfer test, two resized crossing-split denial tests, the full gate and the memory scenarios serve as evidence.
Facts: denial now precedes retained copies on all three shared paths, and the single-crossing split denial still fires at the split-ends list with the pre-reservation held, covering simultaneous old/new storage under denial. The gate holds 2506 core plus 100 visual plus 6 font with no fixture or research movement, and all memory scenarios pass. Ends arrays allocated upstream in MapDiagonalContours are covered by catch-up accounting at Prepare; threading that reservation from the mapper stays the next ownership slice, as does FinishStrokeFill.
Decision: the transfer boundary is retained for the three shared fill paths. Next: mapper-side ends reservations, then raster-loop sharing through the same payload.

## Stroke retention joins the pre-reserved transfer boundary (2026-09-28 implementation)
Question: does the fourth contour owner close the retention transfer boundary without changing stroke behavior.
Method: FinishStrokeFill now pre-reserves exact Scene bytes, copies under cover, and transfers the reservation through RetainPolygonFill into Create adoption; a hand-sized stroke denial test plus the full gate and memory scenarios serve as evidence.
Facts: denial precedes retained and role copies on the stroke path with full release, and the gate holds 2507 core plus 100 visual plus 6 font with no fixture or research movement. The use and symbol remap paths already reserve transient mapped storage before copying and only share source ends and signs conservatively, so they need no change. Remaining transfer work is the upstream mapper ends reservation and any pattern-polygon retention, which keeps its own Create path.
Decision: all four polygon fill retention paths now share reserve-before-allocate with ownership transfer. Next: raster-loop sharing through the same payload, then row 3 calibration.

## Four polygon rasterizers share one mapping path (2026-09-28 implementation)
Question: can the four polygon mapping phases share one boundary without changing pixels or denial behavior.
Method: extracted a MapPolygon local function returning an owned MappedPolygon payload (points, bounds, clip, fractional flag, surface reservation); the solid, linear, radial and pattern loops keep only paint evaluation. Denial order is unchanged since charging, reservation and cancellation points moved verbatim. The full gate, six-output parity tests and memory scenarios serve as evidence.
Facts: net minus 76 lines in SvgConverter with zero warnings; gate holds 2509 core plus 100 visual plus 6 font with no fixture or research movement, and all memory scenarios pass. Per-pixel coverage dispatch is shared through a local evaluator; paint evaluation stays per loop by design: unifying it would need per-pixel polymorphism on the hot path, and row 4 replaces the fan-scan traversal anyway.
Decision: the shared mapping boundary is retained and completes the row-2 consolidation. Next: mark row 2 complete and begin row 3 calibration through its narrow work order.

## Translucent and half-coverage quantum controls agree exactly (2026-09-28 implementation)
Question: do the missing row-3 control cells (translucent 127/128, half coverage, black background) agree with Chrome or bound the calibration.
Method: five admitted-syntax 16x16 quarter and half-edge rect probes (opaque half, alpha-127 and 128 quarters, black-background quarter, alpha-128 half) with three fresh-process captures each, candidate renders and decoded VisualDiff comparison, plus non-vacuity cross-checks against the opaque anchor.
Facts: all five triples byte-stable and all five candidates byte-exact, so no research ratchets were owed; all five graduate to exact fixtures as original-quantum pins (336 entries). The only standing quantum residuals are the opaque 0.75 anchors (191 against 192, transparent and white). Translucent composition therefore already agrees and the calibration scope narrows to opaque mask quantization.
Decision: no product change in this slice; independently useful successes are retained as exact fixtures rather than deleted. Next: the narrowed opaque-mask rule with these five as must-stay-exact guards.

## Tenth-coverage adjudication refutes pure rules and finds path-band jitter (2026-09-28 decision analysis)
Question: does a 0.1-coverage edge adjudicate between floor-quantization and position-offset accounts of the quantum residuals.
Method: three admitted-syntax 16x16 fractional path probes (opaque, alpha-127 and alpha-128 at left 0.1 coverage) with three fresh-process captures each, per-pixel decoded comparison against candidates, plus column profiles of both sides.
Facts: all triples stable. Chrome reads 25 at the opaque left edge and 229 at the right against our 26 and 230, killing the position-offset account; alpha-127 reads 12 against 13 while alpha-128 is exact. No fixed mask-plus-combination rule fits all ten pinned cells (a monotone byte rule cannot send 95.62 to 95 and 31.88 to 32), and no global offset fits edges needing opposite signs, which favors phase-dependent fixed-point truncation noise as row 4 already routes. Separately, our fractional path bands jitter per row (25/26 and 229/230 scattered) while Chrome is uniform and our rectangle bands are uniform: per-pixel fan coverage carries float noise across rows, a spelling-dependent defect owned by row 4 spatial work, not by calibration.
Decision: opaque tenth-edge and translucent-127 tenth-edge stay as admittedRendering ratchets of 12 pixels at max error 1 each; translucent-128 tenth-edge graduates as an exact fixture. No byte-rule product change: none is supported by evidence. Next: row-4 matched fixed-point scan conversion, which owns both the truncation model and the fan jitter.

## Row-4 scoped plan: spatial membership over intervals (2026-09-28 planning)
Question: what is the ordered, bounded route from the current fan scans to spatial winding and matched straight-edge conversion.
Method: mechanism inventory from retained oracle evidence: per-row fan float noise straddling exact half boundaries on straight edges, fringe doubling on duplicate strokes, uniform bands versus jittered fans on identical geometry, and zero-lifting role repair already landed. Fresh re-measurement of the review stroke pair under the current tree reproduces 50 pixels at max error 8 single and 50 at 108 duplicate with identical reference hashes, so the premises hold post row-1 and row-2.
Facts: the duplicate pair stays as admittedRendering ratchets of 50 at 8 and 50 at 108 with bboxes x1..14 y1..14. Ordered slices: first, duplicate and overlapping stroke acceptance (investigate per-subpath separation versus spatial union before changing compositing); second, straight-edge determinism through a matched fixed-point construction with the quarter, half and tenth anchors plus translucent guards as acceptance; third, an interval winding and parity resolver for touching, coincident, nested and crossing contours at the topology level; fourth, full traversal replacement through the shared payload with work charging and during-phase cancellation. Each input keeps comparison against its own reference since the browser itself deltas single versus duplicate by 28 pixels at max error 5. No fixture-specific deduplication or per-pixel correction is acceptable.
Decision: this plan replaces the missing R34 spatial stage and absorbs the redirected row-3 quantum work. Next: the duplicate-stroke investigation slice.

## Duplicate-stroke doubling localized to unsigned contour summation (2026-09-28 investigation)
Question: where does the duplicate-stroke fringe doubling come from, and is per-subpath separation or spatial union the fix.
Method: geometry dump through the internal parse seam for both retained probes, plus the pinned oracle deltas. No product change.
Facts: the single stroke lowers to one PolygonFill with 4 points, ends [4] and roles [1]; the duplicate lowers to one fill with 8 points, ends [4,8] and roles [1,1]. The roles rasterizer sums unsigned per-contour fan areas, so full pixels clamp identically while fringe partials double (0.3 plus 0.3 renders 0.6), which is exactly the retained 50 pixels at max error 108 against 8 for the single. Chrome deltas single versus duplicate by 28 pixels at max error 5, so it double-coats rather than unions: neither our sum nor a pure union reproduces its mechanism, though union bounds the error at Chrome self-delta scale. Per-subpath separation would reproduce double-coating faithfully but splits every multi-contour scene into sequential composites with work, memory and draw-order blast radius; clamping per contour or taking same-sign maxima mispaints disjoint islands sharing a pixel and is rejected. Fixture-specific deduplication is rejected by plan.
Decision: no product change in this slice. The fix belongs to the interval-winding slice with true union semantics, guarded by the retained 50 at 8 and 50 at 108 ratchets with per-input references. Next: straight-edge determinism through the matched fixed-point construction.

## Definition stylesheets admitted; inv-050 renders (2026-09-28 implementation)
Question: can definition-level stylesheets share the root machinery and unblock real logo documents.
Method: traversal hook routing defs-direct-child style elements through the shared stylesheet parser in document order with uniform late-stylesheet rejection; three render tests for application, cross-location source order and the late boundary; re-probes of inv-050, inv-085 and inv-094.
Facts: inv-050 parses fully (stylesheet, inch units and curves admit along the way) and renders at 1080 with whole-image mismatch against its oracle triple (671159 pixels at max error 255 over the full frame); default 2880 output exceeds the polygon-fill raster work budget, a scaling finding for large curve-flattened scenes. inv-085 still rejects on defined local clip-path references and inv-094 on custom mask regions, both owned S14 slices. Two locked old-boundary expectations updated deliberately: the defs-style rejection case removed and a garbage-CSS code expectation corrected to the MalformedInput the shared parser already yields for root styles. Gate holds 2511 core plus 100 visual plus 6 font with no fixture or research movement; memory scenarios pass.
Decision: retained. Next row-7 slices are the S14 diagonal-clip and custom-mask blockers, then inv-050 fidelity at scale.

## inv-050 renders near-exact; residuals are diagonal chamfers (2026-09-28 implementation analysis)
Question: where does inv-050 actually mismatch, and is a scaled render comparable to the cropped native oracle.
Method: native 2880 render under a raised work budget, top-left 1080 crop against the cropped native oracle triple, decoded 6x6 mismatch heatmap with cell-center sampling, plus visual inspection of both images. No product change.
Facts: the valid comparison is 5364 changed pixels (0.46 percent) at max channel error 54 and max alpha 8, against 671159 for the invalid scaled-versus-cropped pairing, which must never be recorded as fidelity evidence. The heatmap traces the diagonal octagon chamfers while fills, stylesheet-bound white letters, counters and straight edges agree, and the candidate is visually complete. Default 2880 output exceeds the 100M-unit raster work budget, so large curve-flattened scenes need budget-scaling work before native comparison.
Decision: inv-050 fidelity follow-ups belong to diagonal coverage in row 4, not to curves or composition; work-budget scaling for large scenes is a separate recorded need. Next: S14 diagonal-clip and custom-mask blockers.

## Quarter-pixel mapping gates lifted; four logos render (2026-09-28 implementation)
Question: is the quarter-pixel viewBox-mapping gate load-bearing, or a stale restriction the fractional rasterizer outgrew.
Method: removed both Map edge checks to exact passthroughs after verifying every caller is raster-side with general fractional support; single locked rejection test repurposed as a third-pixel admission test with an analytically exact pixel; all four unblocked documents rendered and compared against retained or fresh oracle triples.
Facts: full gate holds 2511 core plus 100 visual plus 6 font with the repurposed test as the only delta and no exact-corpus change. inv-029 and inv-034 render at default size with amended-recipe exactness re-verified byte-identical on all six background variants. inv-059 renders with a maple-leaf-region known difference of 2258 pixels at max channel 57 with zero alpha error; inv-080 with a stripe-region known difference of 4532 at max channel 42 with zero alpha error. Tally moves to 27 exact plus 23 known-difference plus 42 unsupported.
Decision: retained. The gates stay deleted; fractional-map fidelity now accrues per-document evidence instead of a blanket rejection. Next: row-5 double-affine context or the S14 clip and mask blockers.

## Circle sampling direction flipped; dash phase and length remain open (2026-09-28 implementation)
Question: does flipping closed-loop sampling direction close the dashed-circle gap, and what remains.
Method: SampleFull sweeps negative angles from the same origin; solid-circle pre/post renders compared byte-identical; offset sweep plus ring-profile alignment in both orientations; single-dash and aperiodic probes with fresh triples; full fixture and research re-measurement with before/after tuples.
Facts: the flip is safe (solid circle identical) and net positive across the dash family (butt 94 to 90, offset 98 to 92 with max error 255 to 241, circle-dash mark 114 to 103 with max alpha 240 to 232, circle-dash-offset mark 113 to 106). The ellipse-dash mark regresses (88 to 93 with max alpha 223 to 255) and is recorded as such. No rotation, reflection or offset of our pattern aligns the single-dash oracle (best 76 at max error 175 with 20-plus mismatches both ways), and our dash sits off the predicted arc while the sampled contour starts a tessellation step past the path origin, so start phase, true-length metric and seam merging stay open as separate follow-ups with the three new probes as acceptance. Completion in the same slice: the fourth mover (ellipse-dash-offset 86 to 77 with max alpha 226 to 123) was re-measured with a hash-matched candidate, a third probe (ellipse single-dash, 54 pixels from a fresh identical triple) was admitted, and the four fixture reason strings were reconciled to the measured tuples.
Decision: the flip is retained with updated ratchets and three new probes; the remaining dash work is start phase, arc-length metric and seam merging in that order. Next: row-7 S14 blockers or the dash start-phase slice.

## Circle flip reverted; sampled loops lead with the origin (2026-09-28 implementation)
Question: is the negative-angle traversal correct, and why does the dash start a tessellation step past the path origin.Method: 512px exact-upscale single-dash control with a fresh Chrome capture and MemoryProbe render; side-by-side block map plus quarter-degree angular histograms on the mid-ring band for solid and faint coverage; direction audit of the sampler parametrization against the measured oracle side; edge-set audit of every SampleFull consumer; origin-first rotation of the loop buffer; full fixture and research re-measurement.Facts: the flipped renderer paints the dash on the north side (mid-ring 263 to 356.5 degrees) while Chrome paints the south side (0 to 91.5 degrees) from the shared 3-o-clock start, so the 0-to-negative-2pi sweep mirrors the browser and the flip is reverted; its ratchet deltas were seam-phase luck on non-integer-period circumferences, with ellipse-dash regressing throughout. The loop buffer rotates its closing origin to the front with an identical cyclic edge set, so fills and solid strokes are untouched while dash measurement begins at the path start like open contours. The 512px control falls from 33476 to 794 changed pixels with mid-ring solid and faint coverage exactly (0 to 91.75, 0 to 92) on both renderers, matching the nominal 256-unit true arc, and residuals sit at both dash-end caps plus a start sliver. Ratchets move to research 76, 80, 34, 21 and 21 with maxima 131, 152, 95, 49 and 49, and fixtures 94, 94, 78 and 76; the full gate holds with no exact-corpus change.Decision: the flip is reverted and the origin-first rotation is retained with updated ratchets; dash-end cap orientation and seam merging remain the follow-ups. Next: the dash-end cap slice, row-7 S14 blockers or row-5 affine core.

## Dashed-circle residue is cap orientation, not cut position (2026-09-28 analysis)Question: after the origin fix, what remains of the 794 changed pixels on the 512px single-dash control, and can a polyline-level fix close it.Method: exact Python mirror of the adaptive sampler (flatness 0.25, r 160) giving 64 vertices with a 5.625-degree first step and chord total 1004.906 against true 1005.310; pattern walk replayed in chord space; alpha block maps plus cap windows at both dash ends for Chrome and ours; code-point audit of butt-cap emission.Facts: the replayed dash cut lands at 91.71 degrees against the true-arc 91.673 degrees, so cut position is exact to 0.1px and the deficit is orientation: butt caps emit perpendicular to the terminal chord per the documented S11 flattened-offset boundary, tilted half a step (2.8 degrees, about 3px across the 64px cap) from the true tangent, while Chrome cuts perpendicular to the curve tangent. Mid-segment cuts cannot be reoriented from polylines because every subsegment keeps the chord direction, and secant tangents would change cap emission for all open strokes, so the correct fix is curve-aware run stroking owned by rows 4 and 5. The 16px probes sit at fringe level (21 at 49, 34 at 95) and cannot move on geometry.Decision: recorded as the scoped dash-end cap slice with the 512px control as acceptance; no kernel or densification hack. Next: that slice when rows 4 and 5 reach curve-aware stroking, otherwise row-7 S14 blockers or row-5 affine core.

## Open-stroke stroking is quad-union, not joined polygons (2026-09-28 analysis)Question: why do open-polyline turns mismatch at full-pixel strength, and what stroking model fits every measured pixel.Method: solid butt-run control with a fresh Chrome capture; straight-plus-offset and natural-phase bisections; per-pixel boundary reconstruction across five dash variants; an inner-butt single-contour attempt with revert; closed-ring control.Facts: the solid run mismatches 6 pixels at max 255 with Chrome filling the inner notch our inner miter vertex carves, while straight-plus-offset stays exact at 8 pixels of max 1, proving the offset convention. Every pixel across the variants fits per-segment butt quads plus outer join wedges unioned: inner butt corners plus outer miter corner, with no inner vertex. The inner-butt-plus-jump single contour fills the notch but its diagonal cuts the cap-row neighborhood and it breaks closed-loop hole corners (ring pixel 255 to 128), so it was reverted the same day; closed loops keep sharp inner corners, which quad-union also produces. Quad contacts are interior or point-only, so the summing filler stays safe here, unlike coincident duplicate strokes.Decision: scoped open-stroke quad-plus-wedge rewrite slice for miter and bevel joins with the pathlength, offset and solid-run controls as acceptance; round joins keep current behavior with own probes and closed loops stay untouched. Next: that rewrite, or row-7 S14 blockers or row-5 affine core.

## Open miter joins trace quad union with outer ears (2026-09-28 implementation)Question: how to stroke open miter and round joins to the pixel-complete quad-union model without breaking closed loops or round behavior.Method: EmitJoin gains an open-contour flag; open miter joins close butt on the concave side selected by positive cross with nonnegative dot and spike the outer side through the miter tip, tracing the wedge ear in one contour; round fans move to the outer side; fan chords cap at thirty degrees; outline capacities rise four to six times points for the ear triples; solid-run, V-shape, acute and rounded controls plus full manifest re-measurement.Facts: the pathlength probe falls 5 to 3 with maxima 102 to 25 as the notch fills, the round-stroke mark 59 to 40, the round-join rounded mark 194 to 184 and the round-join-even probe 56 to 49 with maxima 201 to 13, and the closed-loop dash-offset probe graduates exact as the 338th fixture. The dot term is load-bearing: acute inner turns keep the classic vertex, which the acute oracle matches, while right turns within ninety degrees close butt. Tessellation joints boil plus or minus one pixel honestly on curved dashes. Closed loops, bevel branches, spikes and round caps keep their paths; the full gate holds with no exact-corpus change.Decision: retained with updated ratchets and one graduation; round caps and joins share the finer fan. Next: dash-end caps at scale, row-7 S14 blockers or row-5 affine core.

## Diagonal clip application scoped on inv-085 (2026-09-28 analysis)Question: what exactly must clip-path application compute for the saltire clip, and what remains after geometry.Method: exact vertex walk of the clip path; separate renders of ground, white saltire, red saltire and cross; ghost composite with a binary triangle-union mask under source-over against the staged 1000x600 oracle triple; mismatch attribution by distance to clip edges and diagonal bands.Facts: the clip path is four same-wound plus-187.5 triangles touching only at the shared vertex, unioning to half the viewport under the default nonzero rule. All four parts render. The ghost matches except 5762 pixels at max 143, all inside the diagonal-stroke band (4134) or at clip-edge and diagonal intersections (1628) with zero elsewhere, so clip geometry and part correctness are proven and the residual is the standing diagonal-AA family plus binary-mask clip edges. Rectangle-union stays inapplicable. Sound application needs contour intersection with union semantics, touching-vertex handling from row 4, while diagonal edges ride row 4 and S11; the expected outcome is a Czech-like known-difference render, not exact.Decision: scoped clip-application slice with inv-085 rendering at diagonal-edge residual as acceptance; path-child clip registration rides along. Next: row-4 spatial machinery, row-5 affine core, or the dash-end cap slice.

## Coincident contours merge to first occurrence (2026-09-28 implementation)Question: per-subpath separation or spatial union for the duplicate-stroke doubling, without touching compositing.Method: fringe profiles showing exact doubling with clamp on the duplicate probe against single and oracle; combination-point audit through roles into unsigned fan-area summation; alternative analysis (blanket max-union undercounts touching dash seams, signed winding still doubles); bitwise-identical merge helper with pair charging and per-vertex copy accounting wired into both fill preparation sites before roles and reservations.Facts: the duplicate probe falls 50 pixels at max 108 to max 10, matching the single-probe family plus quantum. The merge fires nowhere else in the corpus: full gate holds 2516 core plus 100 visual plus 6 font with no exact-corpus change. Touching, nested and crossing contours keep existing combination; rotated and reversed identicals need spatial identity. Compositing is unchanged.Decision: retained with the updated duplicate ratchet; crossing and partial overlaps stay row-4 spatial-union work. Next: dash-end caps at scale, row-7 S14 blockers or row-5 affine core.

## Opaque coverage truncates times 256; translucent keeps rounding (2026-09-29 implementation)
Question: does the release-tag Skia truncation rule close the opaque 0.75 anchor without breaking translucent agreement or the exact corpus.
Method: narrowed product change in SvgConverter.CoverageAlpha (opaque alpha 255 truncates coverage times 256 clamped; all other alphas keep round-half-away), eight anchor unit tests, three quarter-edge graduations plus the viewbox-mark-24 graduation, and full fixture/research re-measurement with per-mover fresh MemoryProbe plus VisualDiff confirmation and programmatic record updates.
Facts: the 0.75 anchor moves 191 to 192 with 0.25/0.5/1.0 agreeing and translucent 127/128 controls unchanged; fixtures graduate to 341 entries (260 exact plus 81 unsupported) with no exact-corpus loss. Ratchet deltas are fringe-level and broadly improving (circle-pattern 291 to 289, ellipse-gradient 247 to 241, ellipse-radial 256 to 250, ellipse-pattern 249 to 243, circle-dash 94 to 92, ellipse-dash 74 to 73 with an identical candidate, bevel-join 196 to 195, viewbox-scaled-circle 372 to 361); structural movement the other way (circle-mark 145 to 147) is lateral churn on curve coverage owned by the row-4 fixed-point model. Unit pins move one step each (Double crossing alpha sum 4231 to 4229, fractional self-intersecting pixel 68 to 67). Full gate holds 2524 core plus 100 visual plus 6 font green; memory and package checks pass.
Decision: retained. Truncation versus floor remains indistinguishable on the anchors and floor is kept per the literal source reading; the residual dust hazard is adjudicated by the gate. Next: row-4 interval winding and parity, then dash-end caps at scale.

## General fractional rectangle coordinates admitted; inv-042 renders (2026-09-29 implementation)
Question: is the quarter-pixel rect-coordinate gate load-bearing, or parse conservatism the fractional backend outgrew.
Method: consumer audit of ParseRectangleCoordinate (analytic rectangle rasterizer, ellipse sampling, mask/pattern/image/viewport leaves with downstream gates preserved, gradients on their own ParseAxis path); one-line gate deletion; analytic unit tests with hand-computed truncation alphas; full 92-document Default re-probe with per-file rejection stacks; fresh candidate plus VisualDiff against the re-verified retained oracle triple.
Facts: exactly one disposition flips (inv-042 renders, 5355 bytes, SHA-256 914f7c8f5ee68371154ac39e5c0254582d174ea93d9f0dffed8265da9ab09e04; 91 others stable). The residual is a rounded-region known difference (620 pixels, maximum channel error 46, zero alpha error, bbox x83-248 y183-348 over the ry82.5 rounded rect) in the standing sampled-arc family while near-integer rects compose exactly. The retained 2026-09-27 oracle triple re-captured 2026-09-29 3/3 byte-identical with matching hash. Four locked rejection tests repurposed deliberately (root-parser admission with coordinate retention, circle render, contract-theory case removed, font-budget substitute input). Full gate holds 2524 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Fractional transform args and stroke widths stay gated on their integer carriers for rows 5 and 6; DTD stays prohibited by policy and forward references stay row-7 work.
Decision: retained. Finer-than-quarter fidelity now accrues per-document evidence instead of a blanket rejection. Next: row-7 reference/clip/mask blockers or the row-5 affine carrier.

## Rect-stroke fractional geometry and odd widths admitted; inv-074 renders (2026-09-29 implementation)
Question: are the rect-stroke integer-geometry and even-width gates load-bearing, or shared conservatism the analytic band path outgrew.
Method: backend audit (four-band tiling exact with no corner overlap, double half-widths, gradient/pattern probes reusing the same bands with their own downstream gates); two-line gate deletion with honest messages; unit tests with hand-computed band alphas plus transformed/oversize rejection retention; full 92-document Default re-probe with message-level diff; fresh candidate plus VisualDiff against the re-verified retained oracle triple; per-pixel mismatch-map attribution.
Facts: exactly one disposition flips (inv-074 renders, 353 bytes, SHA-256 b821610ca4d9bb08f78bad142f30b1abc9527f4352eda71ee295a0c6c26c22f2; 91 others stable with identical messages). The residual is a tick-curve known difference (71 pixels, maximum channel error 68, maximum alpha error 27, bbox x8-21 y0-20) and every error is red-family while box-black pixels match exactly, proving the newly admitted bands exact and the residual pre-existing curve-family. The retained 2026-09-27 oracle triple re-captured 2026-09-29 3/3 byte-identical with matching hash. Full gate holds 2528 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Transformed strokes, oversize widths, dasharrays and scaled viewports stay gated; general outline-kernel odd/fractional widths stay row-6 work.
Decision: retained. Rect-stroke fidelity now accrues per-document evidence instead of a blanket rejection. Next: row-7 reference/clip/mask blockers or the row-5 affine carrier.

## Deferred gradient href resolution admits forward references; inv-035 renders (2026-09-29 implementation)
Question: can forward gradient hrefs resolve without changing any backward or dangling behavior or the streaming single-pass architecture.
Method: missing-target inheritances stash as budgeted pending entries (template plus stops and identifier copies) instead of throwing, guarded by exact consultation conditions so every currently-resolving input takes byte-identical paths; a reentrancy-guarded eager drain after every registration plus defs-end and document-end drains resolve chains in dependency order; leftover targets throw the original registration error through a leftover-only bypass flag. Full 92-document Default re-probe with message-level diff; fresh explicit-viewport candidate plus VisualDiff against the re-verified retained oracle triple.
Facts: exactly one disposition flips (inv-035 renders, 28367-byte explicit-800x600 candidate; 91 others stable with identical messages). The residual is a radial-glow known difference (34720 pixels, maximum channel error 2, zero alpha error, bbox x0-799 y273-325) with stripes exact, attributing to the queued transparent-stop interpolation family rather than geometry. The retained 2026-09-26 oracle triple re-captured 2026-09-29 3/3 byte-identical with matching hash. A stored pointer-format bug (HrefId holding the raw href) was found by the minimal probes and fixed before recording. Full gate holds 2530 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Uses preceding target resolution still reject like use-before-def; cycles and dangling hrefs reject with the original errors.
Decision: retained. Gradient reference order now accrues per-document evidence instead of a blanket backward-only rule. Next: row-7 clip/mask blockers, use-before-def policy, or the row-5 affine carrier.

## Fractional axis-aligned transforms admitted; inv-004, inv-006 and inv-072 render (2026-09-29 implementation)
Question: can the integer transform carrier widen to finite doubles without changing any integer-valued behavior or weakening the numeric-range discipline.
Method: AxisTransform carries double scales and offsets with int-range result checks that are provably bitwise-identical for the integer subset (all intermediates below 2^53); translate/scale/matrix arguments parse as finite doubles while rotate/skew functions, zero scales and effective-width integer discipline stay gated; use-instance uniformity checks compare exactly instead of through long truncation. Full 92-document Default re-probe with message-level diff; fresh explicit-viewport and intrinsic candidates plus VisualDiff against re-verified oracle triples; candidate viewing for placement proof.
Facts: three dispositions flip (inv-004 brush-stroke 8350 pixels at max channel 255 with rounded-ground arcs, inv-006 curved-mark 1298 at 255/124, inv-072 scaled-disc 909 at max channel 51 with zero alpha over exactly the disc edge) and three advance with zero regressions elsewhere. One over-strict int-range extent bound was caught by the extreme-orthogonal preservation test along the way (extents were always long-range) and restored. Full gate holds 2537 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Fractional scales compose into stroke widths, gradient scales and magnification only through preserved downstream gates; general outline-kernel widths and spatial unions stay queued.
Decision: retained. Transform fidelity now accrues per-document evidence instead of a blanket integer rule. Next: row-6 widths, row-4 spatial union, or row-7 clip/mask blockers.
## Point-touch contours resolve through subdivision and lobe splits (2026-09-29 implementation)
Question: can contour sets whose only defects are point-touches at identical coordinates render without any epsilon tolerance or computed points, and what guards keep the admission sound. Method: touch-only gate over classifier flags falling back from the crossing splitter; edge subdivision at interior touch vertices with bitwise copies plus ring splitting at shared vertices into lobes sharing one value, reusing the split-loop guard for verification; nesting roles skipping bitwise-shared test vertices for strictly interior samples on fill paths only; full 92-document re-probe with message-level diff plus fresh oracle triples and VisualDiff for both graduations. Facts: inv-008 renders byte-exact (0 px, 288-byte candidate) and inv-045 with a 1567-pixel disc-edge residual at max channel 96 and zero alpha; inv-061 advances past simplicity to closed paths with zero drift elsewhere. Verification caught two real defects in the new code: a spike-drop loop that charged work without comparing (shared-tip check missing) and guard failures that fell through to verifying the unresolved set, which the single-loop vacuous pass then admitted; both fixed with a blocked split signal plus regression tests. Fully-inscribed loops, mixed crossing-plus-touch sets, overlaps and multi-crossing stars stay rejected. Decision: retained with the hourglass, union, hole, evenodd and spike probes as acceptance; planar splitting for crossings and spatial stroke unions stay queued. Next: row-4 planar splits, row-5 affine core, or row-7 clip/mask blockers.
## Inter-loop crossings resolve through planar subdivision (2026-09-29 implementation)
Question: can star pentagrams and other multi-loop crossing sets render by extending the intra-loop split to a planar sweep without touching the admitted paths or the even-odd gates. Method: phase-one intra-loop splits keep their code and outputs; when the split-loop guard still fails under nonzero fill, a second sweep finds one strict inter-loop crossing per round with the shared bounding-box plus straddle plus crossing-point predicates and subdivides both edges at the bitwise-identical computed vertex, each round strictly reducing the crossing count under a round cap; the unchanged guard still arbitrates touches, overlaps and degenerates, and resolved sets reuse the shared-vertex nesting skip. Full 92-document re-probe with message-level diff plus fresh oracle triples and VisualDiff for the graduation. Facts: the integer pentagram paints its full star with edge-fringe residuals only; inv-058 renders with a 3963-pixel disc-region residual at max channel 101 and zero alpha (viewed correct, triple re-verified); inv-044, inv-086 and inv-089 advance to transform, work-limit and use-refs gates with zero drift elsewhere. Verification repurposed the crossing-plus-touch lock: a triangle corner bitwise-identical to the computed crossing becomes a shared vertex and paints union (7 px at max 85 from overlap-summation wrap, a pre-existing kernel property now pinned), while a corner merely near the crossing still rejects. Fully-inscribed faces, mixed non-coincident sets, coincident overlaps, even-odd multi-crossings and stroke unions stay queued. Decision: retained with the pentagram, crossing-squares and split-vertex probes as acceptance; true-union coverage for overlapping loops rides the row-4 spatial-union stage. Next: row-4 spatial union and stroke unions, row-5 affine core, or row-7 clip/mask blockers.
## Identical contours merge before classification (2026-09-29 implementation)
Question: why do repeated identical diagonal subpaths still reject with CoincidentOverlap when the coincident merge exists, and what unblocks them without changing any other admission. Method: flag trace showing the fill-path merge sits after splitting where the overlap flag makes it unreachable (strokes bypass validation so only their merge ever fired); moved the merge with its pair charging and re-reservation ahead of classification and deleted the dead post-split block; per-path and per-group bisection of inv-001 and inv-026 plus loop dumps with exact offline analysis. Facts: identical diagonal twins merge to one loop with structure None and render byte-identical to the single triangle; inv-001 path 0 collapses 10 loops and 101 points to 3 loops and 35 points but keeps CoincidentOverlap plus touches between near-identical arc variants; inv-026 g56 splits 103 points to 105 across 2 lobes whose 3-point lobe sits in a 1e-13 neighborhood of the shared vertex from unclosed-curve endpoints that agree in source but differ after fractional mapping. Full 92-document re-probe shows zero disposition drift with identical byte counts; full gate holds with no exact-corpus change. Decision: retained with the duplicate-twin test; overlap union and proven weld tolerance stay queued as spatial-union and row-5 work. Next: row-4 spatial union, row-5 affine core and tolerance proof, or row-7 clip/mask blockers.
## Shared first vertices can misresolve winding roles (2026-09-29 analysis)
Question: why does the mixed crossing-plus-touch union deviate exactly at the shared pixel (85 vs oracle 170) while every other pixel matches, and is any accumulator wrapping involved. Method: exact per-pixel coverage arithmetic (lobe 127.5 plus triangle 212.5 equals 340.0) against candidate and oracle pixel dumps; code audit of every coverage-to-byte site; instrumental sign read through the parser on the probe. Facts: parsed signs read 1,-1,1: the triangle resolves -1 through its shared first vertex under skip=false while lobe-first vertices stay unshared; Abs masks the sign on singly-covered pixels (triangle-only rows match at 255) but overlap pixels subtract (127.5 minus 212.5 in magnitude gives 85) instead of unioning (170). CoverageAlpha clamps opaque products above 255 and both coverage kernels clamp their totals, and the duplicate probe doubles with clamp, so no wrap site exists and batch-121 overlap-wrap wording is corrected to role subtraction. Fixing the role to +1 would clamp the pixel to 255 (equal and opposite error), so only per-pixel spatial winding (spatial union) closes it; the skip flag stays scoped to resolved sets to avoid destabilizing pinned luck. Decision: recorded with the corrected probe comment; spatial union stays queued. Next: row-4 spatial union, row-5 affine core and tolerance proof, or row-7 clip/mask blockers.
## Editor namedview subtrees skip without svg descendants (2026-09-29 implementation)
Question: can non-empty foreign editor metadata (sodipodi namedview with inkscape page) be tolerated without opening unknown-content admission or weakening traversal budgets. Method: allowlist by element name (namedview only, preserving the public-contract foreign-rect rejection which is structurally identical all-foreign content) plus an eager subtree scan that keeps the unsupported flag on any svg-namespace descendant; the scan mirrors main-loop structural limits, node budget, work, cancellation and the symbol-depth rule, and leaves the reader on the foreign end element. Parses of skip, svg-hiding, depth-limit and node-budget probes plus a render-equivalence probe and a full 92-document re-probe. Facts: inv-016 renders byte-identical with and without its metadata (10293 bytes both ways) and graduates known-difference at explicit 318x400 (3329 px at max 207/207 from fractional root-width mapping, composition viewed correct, triple re-verified); the foreign-rect and foreignObject locks hold; full gate 2562 core plus 100 visual plus 6 font green. Font-request discovery keeps its own flag (only affects font loading; no inventory file pairs text with editor metadata). Decision: retained with the five probes; wider editor vocabularies extend the allowlist only with fresh evidence. Next: row-5 affine core and tolerance proof, row-4 spatial union, or row-7 clip/mask blockers.
## Nested group uses expand inside-out with cycle rejection (2026-09-29 implementation)
Question: can use elements target groups with nested uses (star-field hierarchies) without re-parsing, scene surgery, or losing painter order, and what keeps cycles, scopes and budgets honest. Method: identifier-bearing groups record scene geometry ranges at close (append-only scene keeps indices stable); group pendings expand recursively inside-out with per-path visited sets for cycle rejection, staged inner references, and slot-keyed extra indices that splice multi-geometry expansions at placeholder positions (found by star-count deficit plus branch ablation plus expansion-count traces: ReplaceWithMany evacuates extras past recorded ranges); group-nested definition leaves register outside symbol, pattern, mask and clip interiors under the true-group depth rule. Counts, twins, forward, cycle, gate and collision probes plus a full 92-document re-probe. Facts: inv-089 renders all fifty canton stars (candidate star count 50/50 vs oracle) with a star-edge residual (8182 px at max 69, zero alpha, zero pixels over 100, triple re-verified); one flip with zero drift elsewhere; full gate 2570 core plus 100 visual plus 6 font green; the link-nested lock holds. Decision: retained with the expansion probes; group opacity, clip and mask scopes plus non-group targets stay queued. Next: row-5 affine core and tolerance proof, row-4 spatial union, or row-7 clip/mask blockers.
## Percentage root dimensions resolve against the viewport (2026-09-29 implementation)
Question: can percentage root dimensions be admitted without breaking absolute sizing, style equality or traversal budgets, and what remains for the two percentage-rooted inventory files. Method: SvgRootLength representation (absolute pixels or percentage) from attributes and inline styles with CSS priority and auto leniency; resolution against the explicit viewport or the default intrinsic size with honest nulls at nested, use and font bases; inline width/height parsing, display block painting and stylesheet type/id tolerance; per-path bisection plus millimeter and integer coordinate-rounding experiments on inv-002. Facts: inv-002 advances past dimensions to polygon simplicity (one curved path renders alone; the defect is fractional-band structural complexity, not dust); inv-027 advances past style width to the nested-viewport base message; full 92-document re-probe shows only those two message advancements with identical byte counts; full gate 2580 core plus 100 visual plus 6 font green. A computed-style cascade variant was reverted the same day after it broke twelve use-instance tests by polluting record equality with shape width attributes. Decision: retained with the ten probes; viewport threading for nested bases and true-group style width stay queued. Next: row-5 viewport threading and tolerance proof, row-4 spatial union, or row-7 clip/mask blockers.
## General affine carrier replaces AxisTransform; inv-015, inv-060 and inv-070 render (2026-09-29 implementation)
Question: can the axis-aligned carrier widen to a general affine map without changing any axis-aligned behavior, and which consumers can ride it honestly with axis gates elsewhere.
Method: SvgAffineTransform (finite double a/b/c/d/e/f, SVG matrix convention, rightmost-first Compose with int-range ComposeChecked) replaces AxisTransform at every carrier site; rotate(angle [cx cy]) plus skewX()/skewY() parse while general matrix() stays gated; path/polygon/polyline vertices, uniform strokes and dashes, circles (not true ellipses), use-instance placement/composition and definition-local shape lowering map generally with uniform-scale gates where widths or dash periods must survive; rects, true ellipses, arcs, patterns, gradients, clips, masks and images keep honest axis gates; the orthogonal band path routes rotated contours to the diagonal fill; group snapshots record ancestor-identity plus viewport presence and reject transformed-source group uses instead of misrendering baked site transforms; ParseShapeDefinition lowers under the element-local transform (fixing the baked-composed inconsistency against rect definitions) so use clones exclude definition ancestors. Full 92-document Default re-probe with message-level diff; fresh candidates plus VisualDiff against re-verified retained oracle triples; unit pins for rotation, skew, gates and use-twins plus staged-oracle fringe pins.
Facts: three dispositions flip (inv-015 rotated-path-region 2895 px at max 66/66 over x67-396 y97-425 at 500x500; inv-060 circle-and-spoke-edge 5029 px at max 54/54 full-image at 512x512, residual rides circle-tessellation sagitta plus rotated-spoke fringe; inv-070 small-cross-edge 2232 px at max channel 50 with zero alpha over x133-766 y58-541 at 900x600) with deterministic byte-identical re-renders and all three oracle triples re-verified 3/3 byte-identical with matching hashes; three message advancements (inv-031 and inv-044 past rotate parsing to the transformed-source-group gate, inv-057 past rotate parsing to closed paths) with zero other drift and otherwise identical render byte counts. Review caught one silent admission (double-negative pattern fills via the determinant check, restored to the per-axis gate with a new rejection lock) plus formatting defects (self-using, remark wording, indentation, stray blanks). Full gate holds 2598 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Non-axis general matrix(), rotated rects/ellipses/arcs/patterns/gradients/clips/masks, even-odd multi-crossings and stroke unions stay queued.
Decision: retained with the affine, rotation, skew, gate and use-twin probes; transform fidelity now accrues per-document evidence instead of a blanket axis rule. Next: row-5 viewport threading and tolerance proof, row-4 spatial union, or row-7 clip/mask blockers.
## Transformed group uses un-bake through the ancestor inverse; inv-031 and inv-044 render (2026-09-29 implementation)
Question: can group uses whose source sits under transformed ancestors render by removing the baked ancestor map instead of rejecting, and what keeps mixed geometry, paint and curve density honest.
Method: snapshots record the ancestor transform; expansion maps collected geometries through its inverse (TryInvert with singular/overflow gates) before the existing outer instance mapping, with per-kind un-bake rules (solid polygons map points with clip mapping through the honest axis gate, rects/bands/lines ride axis inverses, rectangle strokes/minified curves/glyphs/paint-servers gate); nested uses keep mapping through full composed references so every level shares the snapshot space by induction. Full 92-document re-probe with per-file disposition, byte-count and message diff against the pre-slice tree.
Facts: exactly two dispositions flip (inv-031 star-edge 963 px at max 24 with zero alpha; inv-044 star-region 3811 px at max 81 with zero alpha, twelve-star ring viewed correct including the reflected half) with zero other drift; both oracle triples re-verified 3/3 byte-identical with matching hashes and both candidates deterministic across rebuilds. Verification caught a real double-application defect first: transformed shape definitions composed the local attribute twice (R36 for rotate(18)), proven by a -25px scanline shift that scaled linearly with ancestor translates and by direct-vs-use image comparison; definitions now lower under identity-inherited context. The inv-031 use render is byte-identical to a directly composed ten-wedge twin, proving the un-bake faithful. Full gate holds 2605 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Viewport-scoped, singular, stroked-rect, minified-curve, glyph and paint-server group content stays queued.
Decision: retained with the inversion, single-application, translated-group twin and gate probes. Next: row-5 viewport threading and tolerance proof, row-4 spatial union, or row-7 clip/mask blockers.
## Nested percentage bases thread in user units; inv-027 renders (2026-09-29 implementation)
Question: what blocks the play-button icon past style width admission, and in which units do nested percentage bases resolve.
Method: explicit-viewport threading through converter, prepared documents, root parser and traversal (null-forwarding overloads); percentage root bases resolve in parent user units (viewBox size, else explicit sides or 300/150 defaults) with absolute roots and absolute nested geometry untouched; explicitly-closed rendered leaves consume element-free subtrees like the self-closing form while any child element keeps the flag. Per-file disposition, byte-count and message diff against the pre-slice tree plus fresh amended-recipe candidate and VisualDiff.
Facts: exactly one disposition flips (inv-027 button-region 2589 px at max 255 with max alpha 162 over the full 572x400 image; triple re-verified 3/3 byte-identical with matching hash; deterministic re-render). Bisection proved the nested-viewport gate, an explicit-close false positive and a device-px bases error in sequence: device-px bases double-map through the single output map (empty renders with correct scene geometry), while viewBox user-unit bases keep scene content in root user space (correct composition). Residual rides the meet-offset fringe plus the corner-curve tessellation family. A skip-subtree variant was reverted the same day after four animation-content contract tests failed; the consume-and-flag form preserves them. Full gate holds 2610 core plus 100 visual plus 6 font green with no exact-corpus change; memory and package checks pass. Omitting root dimensions with nested percentages, defs-side explicit closes and viewport-nested masks stay queued.
Decision: retained with the bases, viewport-twin, user-unit, dimensionless-gate and explicit-close probes. Next: row-5 tolerance proof, row-4 spatial union, or row-7 clip/mask blockers.
