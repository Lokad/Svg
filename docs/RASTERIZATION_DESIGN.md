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
[four-pixel `objectBoundingBox` probe](../tests/Research/linear-gradient-4px.svg),
the retained three-capture Chrome reference gives `DF001F`, `A00060`, `5F009F`,
`2000E0`. Ordinary pixel-centre, sRGB interpolation differs by one channel in
every pixel. A 16-pixel repeat had 12 mismatched pixels, each by one channel,
showing a backend-specific fixed-point interpolation rule rather than a rounding
choice that is safely inferable from the short probe. Linear gradients remain
unsupported until that rule is calibrated across dimensions and color pairs.

## General-path calibration inventory

The research inventory also retains three-capture, byte- and decoded-pixel-stable
Windows-pinned-Chrome references for an
[8×8 diagonal triangle](../tests/Research/diagonal-triangle-8x8.svg),
[cubic arch](../tests/Research/cubic-arch-8x8.svg),
[even-odd hole](../tests/Research/evenodd-hole-8x8.svg), and
[round stroke join](../tests/Research/round-join-8x8.svg). They supply the first
coverage, subdivision, multi-subpath/winding, and outline targets for P1.3. The
current converter asserts `UnsupportedFeature` for each; neither their stable
browser captures nor this inventory admits a general path or stroke feature.

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
area to a 1/256 alpha scale by truncation and snaps values below 8 or above 247
to transparent or opaque. It improves to six changed pixels, but still has a
maximum channel/alpha error of three. Therefore output alpha quantization plus
endpoint snapping alone is insufficient; any next candidate must also model the
edge/scanline calculation and still match this probe exactly.

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
