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
