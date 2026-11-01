# Filter group opacity

Two overlapping half-alpha rectangles isolate group restoration inside an affine
filter capture. There are no curves, gradients, aliases or large coordinates.
The [manifest](capture.json) retains three agreeing healthy, profile-cleaned native
captures per host for each control. Platform-identical PNGs share storage.

| Control | Result on both hosts |
| --- | --- |
| Affine downscale | Exact; a canonical test preserves this composition |
| Affine upscale | Exact; formerly 87 alpha differences, each one level |

Native device-to-global and global-to-device axes round independently. Their
product retains a tiny skew in the upscale case, selecting the low-precision
raster pipeline; the downscale case retains the packed bitmap consumer. Both
are asserted individually. A global alpha change still breaks the downscale case.

[Four compiled native mechanisms](../../Lokad.Svg.Tests/SvgFilterSourceMapTests.cs)
protect exact device-axis products. [Stage evidence](../svgweb/group-opacity-stages.json)
records failing-before results and host/runtime qualification. Each image uses one
conversion path with its unchanged reference. The complete hosted original remains open.
