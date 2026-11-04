# Shader and effect contexts

[The capture manifest](capture.json) freezes all 6,480 authored original pairs
(12,960 inputs), exact source hashes, output scales and independent native image
identities. Sources are reconstructed in
[the rendering tests](../../Lokad.Svg.VisualTests/SvgShaderContextReferenceTests.cs);
no generated SVG or PNG collection is added. These authored inputs are MIT licensed
under the repository license.

The original product combines three paints, four shapes, three widths, three caps,
three placements, five consumers, two output scales and ordinary/non-scaling strokes.
Candidate instances and direct controls keep separate test invocations. Their
equal native images are measured evidence, not permission to discard either graph.

Three healthy fresh pinned browser processes per host replay every original with
its unchanged Blob/Image.decode/canvas recipe: 77,760 exact comparisons to the
original targets. Windows/Linux pixels agree. Each case checks dimensions and every
canonical RGBA8 byte through SHA256; zero-alpha RGB follows `PixelComparer`.
Original PNG identities remain recorded independently of PNG encoder bytes.

Routine rendering keeps 18 original pairs and six independent stage controls
(42 inputs): non-scaling visual bounds and width rounding; isolated geometry/shader
origins; transparent picture padding; zero, fractional and physical-pixel offsets;
source aliases and unfiltered curves. The remaining 12,924 inputs are individually
discoverable with `Category=Research`. The research integrity check joins every
source/options hash to its frozen native target and capture context.

Direct raster-scope, analytic-storage, recorded-bounds and stored-image tests
exercise the shared mechanisms. Existing filter ownership/cancellation/refusal and
retry classes retain distinct allocation paths; this bank uses one canonical
synchronous owned-PNG conversion without repeating that API matrix.

After building Release:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter 'FullyQualifiedName~SvgRasterScopeTests|FullyQualifiedName~SvgAnalyticStorageWindowTests|FullyQualifiedName~SvgCpuRasterContextTests|FullyQualifiedName~SvgFilterStoredImageBoundsTests'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter 'FullyQualifiedName~SvgShaderContextReferenceTests&Category!=Research'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter FullyQualifiedName~SvgShaderContextReferenceTests
```

Broader bounds/map and shader findings remain open outside this original cohort.
[Qualification](../release-baselines/shader-context-qualification.json) records
failing-before counts, retained integration, platform/runtime checks and elapsed
validation times. These wall times include test discovery and are not renderer
performance measurements.
