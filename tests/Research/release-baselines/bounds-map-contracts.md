# Bounds and map contracts

This review traces the existing clip, mask, marker, text and filter descriptors;
it introduces no renderer, preparation cache or coordinate framework. Runtime source
is `ec8c8de7a`; [package qualification](blur-image-identity.json) binds both runtime
assets. The test source is `54c706249`, including the [selection correction](test-selection.json).
Subsequent corrections below retain their own source and validation bindings.

| Boundary | Retained representation and reason | Existing witnesses |
| --- | --- | --- |
| Linear gradient definition and object-box reflection | [Scene-owned paint maps](../../../src/Lokad.Svg/Parsing/SvgLinearPaintMap.cs) retain authored factory endpoints and gradient-local reflection separately from the drawing reference. Subtracting endpoints from 1 before float shader preparation changes short vectors and periodic phase. | [Mechanism/ownership checks](../../Lokad.Svg.Tests/SvgGradientReflectionTests.cs) and [four failing-before native inputs](gradient-reflection.json). |
| Clip resource and consumer | `SvgNativeClipReference` keeps resource `Map` separate from `CanvasMap`. Object-box normalization changes the resource alone. [Canvas preparation](../../../src/Lokad.Svg/Parsing/SvgCanvasDrawMap.cs) retains parent, boundary and source until complete device admission. | [Clip mechanisms](../../Lokad.Svg.Tests/SvgClipCanvasTests.cs), [own-clip native targets](../../Lokad.Svg.VisualTests/SvgOwnClipReferenceTests.cs). |
| Mask target and image region | [Mask targets](../../../src/Lokad.Svg/Parsing/SvgMaskTarget.cs) retain pre-placement objects; [instance masks](../../../src/Lokad.Svg/Parsing/SvgLeafMaskComposition.cs) retain local visual bounds independently. Source paint clips enclose the resource/visual union before placement. Alpha and luminance restoration retain different clip contexts. | [Object/visual vectors and storage refusal](../../Lokad.Svg.Tests/SvgMaskPaintRegionTests.cs), [opaque bitmap control](../../Lokad.Svg.VisualTests/SvgMaskSourceClipReferenceTests.cs). |
| Marker pose and viewport | [Marker placement](../../../src/Lokad.Svg/Parsing/SvgMarkerSyntax.cs) retains pose separately from the content viewport and parent canvas. A [scene-owned stage](../../../src/Lokad.Svg/Parsing/SvgNativeCanvasStage.cs) shares immutable metadata across paints. Complete placement governs admission; finite inverse-normalization intermediates can exceed its range. | [Native matrix vectors](../../Lokad.Svg.Tests/SvgMarkerCanvasTests.cs), [refusal/cancellation/retry](../../Lokad.Svg.Tests/SvgMarkerCanvasOwnershipTests.cs), [paint-binding images](../../Lokad.Svg.VisualTests/SvgMarkerPaintBindingReferenceTests.cs). |
| Text layout and recorded ink | [Glyph recording](../../../src/Lokad.Svg/Parsing/SvgTextGlyphPainter.cs) keeps source objects, locally enclosed drawing visuals and fractional element visuals distinct. Already-shaped glyphs, fitting and font owners remain authoritative. Mask font construction uses its resource context; consuming placement remains separate. | [Mask text](../../Lokad.Svg.VisualTests/SvgMaskTextRecordingReferenceTests.cs), [text clips](../../Lokad.Svg.VisualTests/SvgTextClipReferenceTests.cs). |
| Recorded drawing and image destination | The [drawing map](../../../src/Lokad.Svg/Rendering/SvgDrawingMap.cs) keeps double parameter composition separate from native destination concatenation. The canvas descriptor expresses a different rounding boundary and cannot replace it. [Filter playback](../../../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) keeps requested/captured/stored/sampled extents separate. | [Captured canvas](../../Lokad.Svg.Tests/SvgCapturedCanvasCoordinatesTests.cs), [native source maps](../../Lokad.Svg.Tests/SvgFilterSourceMapTests.cs), [device paths](../../Lokad.Svg.Tests/SvgPathDeviceMappingTests.cs). |
| Geometry paint clip and source-picture viewport | The existing raster frame borrows the incoming device domain for geometric paint clips across cropped source storage; isolated layers use their own device. Source-picture viewport masks retain their separate classification domain. Thin lines retain their bitmap origin while clipping to the rectangle's integer extent before fixed-point conversion. | [51 failing-before inputs and the independent source-context bank](geometry-paint-clip-domain.json). |
| Native device and addressable layer | Source pictures retain the incoming device and complete source extent; owned layers keep their logical native clip on the existing raster scope. Binding still exposes only compact, addressable storage and its owned interior. Viewport and paint classification consult the current native owner. | [Nine viewport fill cases, direct scope contract and contained-line control](native-clip-device-domains.json). |

The existing scene/registry owns retained descriptors and reservations; references
borrow them. Mask replay transfers content into its adopting scene, while marker
stages remain immutable for raster readers. No new owner or allocation path is
needed to express these boundaries. Equal output alone does not justify combining
the descriptors or their numerical routes.

The selected existing checks pass on Windows/Linux under .NET 11 and .NET 10,
using the same qualified package assets: **33 core** matrix, bounds and ownership
checks plus **47 visual** cases per host/runtime. The visual selection retains
each platform target and the existing raw/prepared interactions. No source input,
reference, tolerance, assertion or text-layout implementation changes.

Use `dotnet test <project> -c Release --no-build --framework <runtime> --filter '<selection>'`,
with `tests/Lokad.Svg.Tests` or `tests/Lokad.Svg.VisualTests` and `net11.0`/`net10.0`:

- Core: `FullyQualifiedName~SvgClipCanvasTests|FullyQualifiedName~SvgMaskPaintRegionTests|FullyQualifiedName~SvgMarkerCanvasTests|FullyQualifiedName~SvgMarkerCanvasOwnershipTests|FullyQualifiedName~SvgCapturedCanvasCoordinatesTests|FullyQualifiedName~SvgFilterSourceMapTests|FullyQualifiedName~SvgPathDeviceMappingTests`.
- Visual: `FullyQualifiedName~SvgMarkerPaintBindingReferenceTests|FullyQualifiedName~SvgOwnClipReferenceTests|FullyQualifiedName~SvgMaskTextRecordingReferenceTests|FullyQualifiedName~SvgMaskSourceClipReferenceTests|FullyQualifiedName~SvgTextClipReferenceTests`.

This closes the descriptor/contract review for these qualified boundaries, with no
new witnessed runtime discrepancy. It does not qualify every historical source or
close invocation/preparation separation and allocation review. Further changes need
a concrete missing boundary or unnecessary allocation/copy, preserving these witnesses.

The scene-replay follow-up uses the [qualified renderer](shader-polygon-paint.json).
[SvgRoot](../../../src/Lokad.Svg/Parsing/SvgRoot.cs) is a readonly value type:
command slices and mask scene views borrow existing arrays and resource indices.
[Vector content](../../../src/Lokad.Svg/Parsing/SvgVectorContent.cs) remains parent-owned
through replay. Mask/group pixel buffers retain the output stride and independent
construction/paint clips; smaller storage requires a complete stride/origin change.
This review establishes no redundant root allocation or need for another replay context.
