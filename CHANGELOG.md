# Changelog

## Unreleased

- Share polygon contour storage across paint bindings and transfer its memory
  charge during repainting and mask stamping, avoiding refusal caused by charging
  reused arrays twice. Preserve source ownership when paint or placement fails.
- Transfer instance-mapping reservations to retained polygons instead of charging
  the same mapped arrays as both temporary and scene storage. Borrow definition
  contours during temporary inherited-fill preparation, preserving source ownership.
- Remove the obsolete destructive polygon-clip backend; retain native clip scenes
  and their source geometry, transforms and coverage.
- Preserve recorded path-baseline metrics and per-glyph pattern matrices through
  texture sizing and sampling, correcting fractional-scale text paint seams.

Development targets 0.4.0. Published 0.3.0 and its source tag remain unchanged.

- Multi-target .NET 10 and .NET 11; use the pinned .NET 11 RC SDK for development
  and extensive qualification, with explicit .NET 10 compatibility checks.
- Add opt-in host-configured HTTP font packs with coalesced acquisition, verified
  program caching, structured failures and shared memory admission. Retain one
  document preparation and dispose governed PNG results to release their charges.
- Remove PNG encoding, parser and raster fuel quotas; retain structural, numerical,
  size/admission and cancellation contracts. Stop compressor finalization from
  retrying a refused allocation and keep canceled symbol sealing transactional.
- API migration: remove `maximumAccountedBytes` and `maximumWorkUnits` from
  `SvgRenderLimits` constructor calls and stop reading their properties. Limits
  now describe input/output and structure. Borrow host admission with
  `SvgRenderOptions.WithMemoryBudget`; governed retained output uses disposable
  `ConvertToOwnedPng`/`ConvertToOwnedPngAsync` results. Font snapshot overloads
  accept the host budget/options and retain metadata charges until disposal.
  The memory probe uses `--memory-budget-bytes` and removes the former quota options.
- Fail memory admission rather than selecting storage-pressure rendering paths;
  retain direct numerical routes and independent native references.
- Correct partially transparent backgrounds and prevent zero-alpha background
  RGB from tinting rendered content.

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
