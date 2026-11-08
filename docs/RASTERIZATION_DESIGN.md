# Rasterization design

Lokad.Svg prepares static SVG into an owned scene, rasterizes it with managed
kernels and encodes PNG. The [support matrix](SUPPORT_MATRIX.md) owns accepted
syntax and resource contracts; [test guidance](../tests/README.md) owns qualification.
Rendering requires no browser, native rasterizer or machine-font service.

## Pipeline and ownership

| Stage | Responsibility | Implementation |
| --- | --- | --- |
| Invoke | Validate calls and manage operation/output lifetimes. | [Invocation](../src/Lokad.Svg/SvgConverter.Invocation.cs) |
| Prepare | Parse bounded XML/CSS; resolve sizing, inheritance, definitions and fonts. | [Preparation](../src/Lokad.Svg/Parsing/SvgDocumentPreparation.cs) |
| Retain | Own geometry, source commands and resource dependencies. | [Scene](../src/Lokad.Svg/Parsing/SvgRoot.cs) |
| Rasterize | Construct coverage, evaluate effects, sample paints and composite. | [Converter](../src/Lokad.Svg/SvgConverter.cs) |
| Encode | Write owned PNG storage, caller memory or a stream. | [Encoder](../src/Lokad.Svg/Encoding/PngEncoder.cs) |

Raw and prepared calls share lowering. A prepared document retains one scene/font
operation and permits one conversion. Resources borrow immutable child scenes;
pattern graphs validate cycles/depth before replay. Geometry repainting transfers
existing storage charges; independent copies reserve their own storage first.
Scratch ends with execution; owned PNG results retain storage until disposal.
[HTTP font packs](HTTP_FONTS.md) have separate resolver/cache lifetimes.

## Coordinates and bounds

| Domain | Responsibility |
| --- | --- |
| Logical viewport/viewBox | Layout and percentage bases, independent of output density. |
| Root image layout/projection | Fractional CSS layout and destination placement. |
| Object bounds | Pre-clip geometry/layout for normalized resources. |
| Visual/recording bounds | Local stroke, font and effect extents before placement. |
| Construction bounds | Source operands and the owning bitmap's clip/origin. |
| Storage windows | Retained rows and exclusive writes. |
| Clip coverage | Actual coverage, independently of its enclosure. |
| Sampled image | Content, padding, origin, color space and sampling view. |

The [drawing map](../src/Lokad.Svg/Rendering/SvgDrawingMap.cs) preserves parameter
coordinates and device coefficients. The
[canvas map](../src/Lokad.Svg/Parsing/SvgCanvasDrawMap.cs) preserves parent, boundary
and source stages. Compose transforms and subtract bitmap origins at their native
stages: flattening first changes float rounding. Global containment does not prove
bitmap-local containment. Storage crops must not change complete-path dispatch.

Invisible or collapsed paint can retain bounds needed by enclosing effects;
non-displayed instances contribute none. Resource normalization does not transform
the consumer. The [bounds contract](../tests/Research/release-baselines/bounds-map-contracts.md)
binds these distinctions to source owners and independent witnesses.

## Geometry and native raster routes

Retain authored lines, curves and conics until placement and
[stroke construction](../src/Lokad.Svg/Rendering/SvgNativePathStroke.cs). Construct
dashes in their owning bitmap, retaining phase and `pathLength` calibration.
Early flattening changes joins, extents and fringe coverage.

[Analytic coverage](../src/Lokad.Svg/Rendering/SvgAnalyticCoverage.cs) retains distinct
rectangle, hairline, convex/compound and fallback arithmetic. Byte alpha narrows
before additive saturation; border/fringe contributions survive storage trimming.
Traversal borrows immutable geometry and shares coverage scratch across paint kinds.

## Paint and compositing

Resolve fill/stroke independently, then apply paint order and element opacity.
User-space paints retain definition/reference placement; normalized paints bind
the source object box before placement. Gradient stops retain precision, ordering
and duplicate boundaries. Pattern sampling repeats each tap before clamping.

[Shader sampling](../src/Lokad.Svg/Rendering/SvgShaderCoordinates.cs) and
[blending](../src/Lokad.Svg/Rendering/SvgAnalyticPaint.cs) preserve their actual
premultiplication, coverage, color-transfer and byte-storage stages. Prepare shared
sampling state within each draw; retain bitmap-local dither origins and distinct
solid, shader, mask and fringe arithmetic.

## Embedded images

PNG decoding validates decompression, then reconstructs scanlines in the admitted
buffer. Uniform images retain color; varied pixels transfer to a premultiplied
raster owner. Decoded stride, fractional source windows, sampling views and clips
remain distinct. Recorded/picture draws retain their mipmap and alpha rules;
strict subsets clamp sampling. Uniform color alone does not justify replacing an
image draw with a solid paint.

## Resources and effects

Compose [filter, mask, clip, opacity](SUPPORT_MATRIX.md#clips-masks-and-composition)
in order. Ancestor clips surround child filter output; source-local clips remain
inside. Partial-opacity groups isolate unless direct replay is equivalent.
Geometric clips retain curves, winding and ordered operands. Successive clips
multiply coverage separately. Masks composite children before alpha/luminance
conversion; replay frames restore maps, coverage and allocation bounds together.

[Filter playback](../src/Lokad.Svg/Rendering/SvgFilterPlayback.cs) propagates bounds
forward and sampling requests backward. Validate all declarations/references, but
evaluate pixels and capture halos only along final-result dependencies. Captures
borrow immutable scene command ranges; capture owners outlive restoration.

Keep logical bounds, stored content, initialized padding and requested sampling
views distinct. Cropping invalidates padding knowledge; identity kernels do not
prove safe aliasing. Empty inputs can still produce output through flood or color
bias. Operating color space and deferred color programs survive aliases and offsets;
apply required transfers/clamps before edge coverage, clipping and enclosing opacity.
Final restoration consumes deferred state once.

## Supplied fonts

Layout retains source positions, emitted glyph/cluster ranges, font selection and
paint boundaries. Case conversion preserves source addressing; shaping can join
compatible face/size intervals. Relative offsets, fitting, absolute positions and
chunk anchors remain ordered. Unpainted characters can still affect layout bounds.

Text decorations and glyph paint share source-fragment ordering, with separate
path placement and ink exclusions. Windows fitting/LCD and Linux light-fitting/
grayscale retain independent references. Large transformed glyphs use canonical
unhinted paths. Metrics are operation-local and budget-owned; font programs remain
immutable. The [text contract](SUPPORT_MATRIX.md#text-and-fonts) owns shaping limits.

## Limits and cancellation

[RenderBudget](../src/Lokad.Svg/Rendering/RenderBudget.cs) admits storage before
allocation. Growth accounts for overlapping buffers; dependencies stay charged
through their owner's lifetime. Traversal, raster and output loops observe
cancellation. Failure releases owners after borrowers finish; refusal ends the
operation. Borrowed inputs remain valid. Streams stay open and may contain partial
output. Bare-memory convenience output is ungoverned; owned output retains a
separate scope until disposal. The [accounting contract](SUPPORT_MATRIX.md#resource-limits-and-memory-accounting)
owns public limits and lifetime guarantees.
