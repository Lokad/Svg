# Changelog

## 0.4.0 - 2026-11-08

Published on [NuGet.org](https://www.nuget.org/packages/Lokad.Svg/0.4.0).
Source tag: `v0.4.0`.

- Multi-target .NET 10 and .NET 11.
- Add opt-in HTTP font packs with verified caching, structured acquisition failures
  and shared memory admission.
- Extend Unicode casing and Latin combining-mark shaping; support supplied text in
  clip paths. Correct text positioning, decorations, paint and Windows glyph fitting.
- Add all four color-matrix modes; correct filter crops, offsets, shadows, source
  placement and alpha/color rounding.
- Correct transformed strokes, gradients, patterns, clips, masks and embedded-image
  sampling, including fractional scaling and partially transparent backgrounds.
- Tighten applicable CSS/length validation, ignore inert static content consistently,
  and reject unsupported PNG color-space declarations.
- Reduce duplicate geometry, image and raster scratch storage; avoid evaluating
  unused filter stages and repeating shader setup. Make package ZIPs reproducible.

**Breaking API changes:** remove `maximumAccountedBytes` and `maximumWorkUnits`
from `SvgRenderLimits` calls and stop reading their properties. Constructors now
require six, seven or eight explicit arguments; consumers must recompile. Encoding,
parser and raster fuel quotas are removed; structural limits and cancellation remain.
Use `SvgRenderOptions.WithMemoryBudget` for host admission and dispose governed
`ConvertToOwnedPng`/`ConvertToOwnedPngAsync` results. New font-snapshot overloads
accept budgets/options and retain metadata charges until disposal; existing
overloads remain. Failure-stage switches must handle `Transport`, `Timeout` and
`Size`. The memory probe replaces quota options with `--memory-budget-bytes`.

## 0.3.0 - 2026-10-28

Published on [NuGet.org](https://www.nuget.org/packages/Lokad.Svg/0.3.0).
Source tag: `v0.3.0`.

- Expand ordered CSS font-family selection, host-defined generic mappings,
  coverage-based fallback and bounded TrueType/GPOS/GDEF compatibility.
- Preserve text-node and paint ranges through shaping, positioning, fitting,
  text paths, object bounds and resource replay.
- Keep source object boxes, visual enclosures, clip dependencies and recorded
  paint maps distinct through local instances, patterns and masks.
- Correct fractional glyph-stroke sampling bounds and dimension accounting;
  share vertical-baseline rounding and axial fitting across fills and strokes.
- Preserve ordered clip unions, clipped curve/stroke admission and marker canvas
  stages without widening numerical or resource limits.
- Correct filter crops, flood/offset/shadow composition, color transfer and
  source-image sampling while retaining independent paint/clip coverage.
- Preserve embedded-image source windows, mip sampling, alpha and instance scopes;
  validate PNG structure and bounded decompression.
- Share bounded document preparation and geometry traversal, reduce redundant
  parsing/storage, and retain cancellation, cleanup and output-ownership contracts.
- Add independent Windows/Linux references and direct numeric/ownership checks;
  keep development checks separate from full qualification.

## 0.2.0 - 2026-10-18

Published on [NuGet.org](https://www.nuget.org/packages/Lokad.Svg/0.2.0).
Source tag: `v0.2.0`.

- Add `SvgConverter.InspectDimensions` to inspect CSS-resolved root dimensions,
  viewBox/aspect mapping, logical viewport and fallback provenance without fonts
  or raster allocation. Inspection and conversion share dimension resolution.
- Support a single root dimension without a viewBox using the other axis's
  existing default (300 pixels wide or 150 pixels high).
- Add opt-in `SvgRasterBounds` and `SvgDocumentDimensions.GetRasterSize` for
  caller-controlled dimension/pixel caps without changing logical layout.
  Preserve native text fitting at fractional resolution and viewport clipping
  when a minimum one-pixel canvas is required.
- Add bounded SVG source diagnostics for unsupported constructs and sizing
  failures, including original locations through retained text and local uses.
  Font failures include requested weight/style, fallback status and missing
  Unicode scalar/index details when available.
- Select GPOS kerning through the default Latin language system with DFLT
  fallback, allowing feature tags reused by other language systems.
- Extend bounded static TrueType fitting with relative moves, CVT deltas and
  oblique projection/freedom vectors. Preserve Windows metric initialization,
  ClearType compatibility and signed fixed-point rounding; qualify unchanged
  Liberation Sans and DejaVu Sans labels against exact Windows/Linux references.

## 0.1.0 - 2026-10-17

Published on [NuGet.org](https://www.nuget.org/packages/Lokad.Svg/0.1.0).
Source tag: `v0.1.0`.

Initial release of the managed SVG-to-PNG renderer for .NET 10 on Windows and
Linux, with no runtime package dependencies.

- Affine geometry and local instances; solid, gradient and pattern fills/strokes;
  caps, joins, dashes and non-scaling strokes.
- Nested viewports, clips, alpha/luminance masks, group opacity, embedded PNGs,
  static CSS, markers and selected filters.
- Explicit supplied-font catalogs and fallback, TrueType labels and text paths;
  regular, bold, real italic and intentional fallback qualification.
- Synchronous/asynchronous output to owned memory, caller memory or streams,
  plus explicit preparation through `SvgPreparedDocument`.
- Size and structural limits, approximate memory accounting, cancellation, deterministic
  cleanup and documented borrowed-resource lifetimes.
- Frozen Windows/Linux visual references, public font checks and installed-package
  consumer verification. Packages include the icon, README, changelog, licenses,
  notices and a separate portable-PDB symbol package.
- NuGet output under `artifacts/nuget/`, with non-Release packaging rejected by
  default.

See the [support matrix](docs/SUPPORT_MATRIX.md) for the implemented static SVG
subset, font/shaping restrictions and explicit unsupported features.
