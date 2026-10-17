# Bounded raster sizing

Three focused outputs preserve logical layout while changing raster resolution:
the same nested percentage viewport, patterned local instance, clip and supplied
text at 600×300 and 800×400; and a 120×1 viewport fitted onto an 8×1 canvas with
an overflowing rectangle. The last case retains an empty native binary viewport
clip instead of exposing content in the minimum one-pixel canvas.

The [manifest](manifest.json) records the original logical sizes, output scales,
unchanged inputs and three healthy fresh-process captures on each platform.
References were frozen before renderer comparisons. The fractional-scale text
case protects native strike-matrix relaxation independently of logical font size.
The [existing Noto Sans program, manifest and OFL license](../supplied-font-labels/fonts/OFL.txt)
are reused; no machine fonts are selected.

The two original SVG mechanism controls in this directory are dedicated to the
public domain under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).
