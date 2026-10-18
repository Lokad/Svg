# Changelog

## Unreleased

- Extend bounded TrueType hinting with physical super-rounding, projection
  vectors, coordinate/relative moves, MIN/MAX and ODD/EVEN, preserving native
  Windows/Linux rounding and glyph-local state.
- Carry instructed curve-tag changes through simple and composite outlines,
  and allow valid relative jumps across balanced conditionals.
- Parse ordered CSS font families and add explicit host generic/fallback policy
  for font snapshots and prepared documents, preserving legacy catalog selection.
- Select supplied-font coverage per admitted scalar, lazily acquiring later
  authored families before host fallback. Preserve mixed-face advances, spans,
  positioning and text paths, with bounded acquisition and meaningful failures.
- Correct valid instruction-free TrueType metric origins and native Windows
  simple dropout control, including the interaction between both scan passes.
- Preserve quoted font-family punctuation through inline/stylesheet priority,
  brace and comment handling.
- Match native signed half-unit scaling in TrueType points/control values and
  bound Latin light edge merging by the font's measured standard stem width.
- Select Windows natural/symmetric LCD profiles from font gasp data at actual
  raster size, and preserve native curve, smart-dropout and stub behavior.

## 0.2.0 - 2026-10-18

Published on [NuGet.org](https://www.nuget.org/packages/Lokad.Svg/0.2.0).
Source: [`v0.2.0`](https://github.com/Lokad/Svg/tree/v0.2.0).

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
Source: [`v0.1.0`](https://github.com/Lokad/Svg/tree/v0.1.0).

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
- Size/work limits, approximate memory accounting, cancellation, deterministic
  cleanup and documented borrowed-resource lifetimes.
- Frozen Windows/Linux visual references, public font checks and installed-package
  consumer verification. Packages include the icon, README, changelog, licenses,
  notices and a separate portable-PDB symbol package.
- NuGet output under `artifacts/nuget/`, with non-Release packaging rejected by
  default.

See the [support matrix](docs/SUPPORT_MATRIX.md) for the implemented static SVG
subset, font/shaping restrictions and explicit unsupported features.
