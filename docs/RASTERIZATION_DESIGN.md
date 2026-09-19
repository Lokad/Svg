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
