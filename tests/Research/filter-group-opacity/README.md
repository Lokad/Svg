# Filter group opacity

Two overlapping half-alpha rectangles isolate group restoration inside an affine
filter capture. There are no curves, gradients, aliases or large coordinates.
The [manifest](capture.json) retains three agreeing healthy, profile-cleaned native
captures per host for each control. Platform-identical PNGs share storage.

| Control | Result on both hosts |
| --- | --- |
| Affine downscale | Exact; a canonical test preserves this composition |
| Affine upscale | Open: 87 alpha differences, each one level |

An experimental global switch to the existing low-precision shader alpha operation
closes the upscale case and the larger integer-viewport control, but breaks this
downscale control and other exact images. It is rejected. Matching those pixels
does not establish what selects the native restore path.

[Stage evidence](../svgweb/group-opacity-stages.json) records the separate native
operations and remaining investigation. These two images use one conversion path;
the open case has no passing rendering assertion or tolerance allowance.
