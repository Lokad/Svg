# Filter source contexts

[The manifest](capture.json) retains all 4,608 authored rectangle/path pairs
(9,216 inputs), source hashes, output scales and independent native targets.
[The rendering tests](../../Lokad.Svg.VisualTests/SvgFilterSourceContextReferenceTests.cs)
reconstruct the exact original bytes; no SVG or PNG collection is added. These
authored inputs use the repository's MIT license.

The original product combines four paints, two zero-radius corner spellings,
three widths, four placements, three consumers, four effects, two output scales
and two fill paints. Both roles remain separately discovered. In 1,110 pairs the
native images differ: comparison always uses each source's own target.

Three healthy fresh pinned processes per host reproduce all original pixels with
their unchanged Blob/Image.decode/canvas recipe: 55,296 exact comparisons. Each
test checks dimensions and complete canonical RGBA8 SHA256, retaining the existing
zero-alpha RGB convention and separate original PNG identities.

The 39 routine inputs comprise 13 original pairs and 13 stage controls. They cover
recorded AA fringes, translated/rotated thin rectangles, wide strokes in nested
viewports, source aliases, physical-pixel offsets, isolated opacity, separate fill
and stroke, deferred color filters and effective-zero/materializing blur paths.
The remaining 9,190 originals and one source/context integrity check retain
`Category=Research`. [The routine selection](routine-cases.json) lists only the
small retained set; discovery still emits every original separately.

Direct source-hint tests separate clip domains, numerical parsing and alias
resolution. Existing filter ownership/refusal/cancellation/cleanup/retry tests
retain their distinct allocation paths. Rendering uses one canonical synchronous
owned-PNG conversion. The [qualification](../release-baselines/filter-source-context-qualification.json)
records before/after results, native evidence and milestone checks.

After a Release build:

```powershell
dotnet test tests/Lokad.Svg.Tests -c Release --no-build --filter FullyQualifiedName~SvgFilterSourceHintTests
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter 'FullyQualifiedName~SvgFilterSourceContextReferenceTests&Category!=Research'
dotnet test tests/Lokad.Svg.VisualTests -c Release --no-build --filter FullyQualifiedName~SvgFilterSourceContextReferenceTests
```

Broader shader and composition findings remain open outside this named screen.
