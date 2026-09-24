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