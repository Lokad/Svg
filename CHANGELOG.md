# Changelog

## Unreleased

- Add `SvgConverter.InspectDimensions` to inspect CSS-resolved root dimensions,
  viewBox/aspect mapping, logical viewport and fallback provenance without fonts
  or raster allocation. Inspection and conversion share dimension resolution.
- Support a single root dimension without a viewBox using the other axis's
  existing default (300 pixels wide or 150 pixels high).

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
