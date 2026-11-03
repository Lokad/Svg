# Changelog

## Unreleased

- Keep groups inside clip resources dormant while preserving their IDs for
  independent geometry and font-bearing instances.
- Admit validated `line-height` on explicitly positioned text without changing
  layout; require contiguous number/unit tokens in inert typography.
- Add default Unicode lowercase and source-context final-sigma handling across
  spans and paths; keep font discovery's whitespace and text lifetimes distinct.
- Add pinned Unicode uppercase conversion with original SVG character addressing,
  transformed font discovery and governed expansion storage.
- Preserve a uniform child anchor when joining direct text and text-path ranges,
  using the same source-style comparison as interval compaction.
- Preserve ligatures across anchor-only style intervals that select the same
  font face and size, retaining source positioning and actual shaping boundaries.
- Admit inert `text-align` declarations on supported preformatted text and
  containers, preserving anchors and explicit positioning.
- Preserve the parent bitmap's drawing-matrix rounding when capturing filtered
  paints, including draws without a recorded transform chain.
- Preserve antialiased coverage beside bitmap borders when an output row is
  narrower than the mask's complete scan window; reserve span scratch separately.
- Ignore unknown and inapplicable SVG attributes consistently while retaining
  validation of applicable rendering features.
- Use initial gradient units for invalid values, preserving template explicitness.
- Respect Windows tiny-glyph program admission and default scanner thresholds,
  preserving scaled-child execution and instruction-free parent scanner state.
- Produce reproducible NuGet and symbol-package ZIP timestamps.
- Correct Windows glyph contours when a font applies conditional vertical pixel shifts.
- Keep Windows fitted glyph originals/current points in their aligned metric frame;
  preserve the child paint origin independently of composite parent-program phantoms.
- Resolve CSS/presentation text anchors through shared inheritance and chunk
  boundaries, preserving anchor-only span changes during interval compaction.
- Preserve native fixed-point precision for expanded Windows initial glyph
  coordinates in both simple outlines and composite children.
- Admit inactive hover/active/focus selectors and inert float declarations in SVG
  images. Share inline-style source diagnostics and preserve stylesheet locations.
- Ignore inert event attributes and character-data script elements during static
  conversion. Preserve switch/pattern selection, lexical node accounting and text
  boundaries without executing scripts, resolving their URLs or retaining bodies.
- Resolve GPOS extension payloads consistently through validation and kerning;
  execute the TrueType freedom-from-projection instruction without changing other state.
- Respect unitless font sizes in SVG inline styles while preserving stylesheet
  length admission, inherited/span styles and prepared font requests.
- Share foreign-description traversal and pattern declaration locations across
  preparation and lowering; preserve the original unsupported-source diagnostic
  when a later text node encounters the rejection.
- Share fixed CSS length conversion across roots, markers and stroke metrics;
  admit physical units for stroke widths, dash entries and offsets, including zero
  widths during font/resource selection.
- Retain shallow authored curves that flatten to a single segment across solid,
  gradient, pattern and clip fills, preserving their ink against the closing chord.
- Preserve parent/child canvas concatenation across path clips, keeping resource
  normalization separate from the referencing transform and clip construction.
- Materialize clip-child transforms before reference and device placement,
  retaining curve controls, conic arcs and independent clip enclosures.
- Keep analytic coverage intermediates as byte alpha before additive accumulation,
  preserving cubic fringes and filtered sources while reducing row scratch storage.
- Preserve fractional root image layout before uniform output scaling, and apply
  image-boundary coverage at each paint or isolated-layer restoration without a
  full-canvas clip allocation.
- Admit nonuniform viewport maps of clipped strokes with an unpainted fill channel;
  retain source-command requirements for painted curves.
- Preserve color-filter shader rounding when black or SourceAlpha filter results
  restore over backdrops, including Gaussian aliases.
- Preserve authored curves and placement in referenced clip children.
- Capture filtered sources per destination bitmap and preserve source-image padding
  during geometry and clip-resource construction; restore each output pixel once
  across bitmap borders.
- Preserve gradient declarations, local/reference matrices and recorded bitmap
  stages across fills, strokes and instances, including clipped and inherited paints.
- Preserve opaque-gradient source reduction and coverage interpolation, retaining
  conical/translucent programs and distinct direct-span/A8 mask rounding.
- Retain completed curve-strip fringes within the paint clip, keeping mask storage
  and clip-definition construction bounded independently.
- Preserve visible-source remapping before residual scaling for clipped fractional
  root images, including the distinct float rounding stages.
- Retain authored visual bounds and recording/destination maps through filter-source
  capture, preserving fractional-root gradients and zero-blur aliases.
- Use complete drawing references for gradient source captures, preserving source
  colors through nonzero blur on fractional root images.
- Enclose recorded primitive strokes before placement and bind hairlines to the
  active layer's raster scope, preserving isolated opacity and captured-source origins.
- Preserve radial factory coordinates through primitive and instance placement,
  sharing retained local/reference maps instead of rescaling circle fields.
- Preserve source path bounds across clipped stroke lowering; retain filter image
  padding, cropping and sampling colour transfer through affine restoration.
- Compose affine image placement in the owning bitmap and store enclosing-opacity
  pixels before packed alpha modulation.
- Skip empty affine-filter restoration areas within software tiles, preserving
  painting in intersecting tiles.
- Resolve explicit local paint fallbacks consistently across fills, strokes,
  inherited/instanced content and prepared text, preserving selective font acquisition.
- Resolve gradient percentages and defaults in each definition or alias viewport;
  retain authored units through unit overrides and partial cross-kind templates.
- Inherit radial center, radius and focal coordinates independently, retaining
  original definition coordinates and sharing storage only for unchanged aliases.
- Accept bounded identity metadata on gradient stops; preserve stop values and
  inherited linear/radial painting.
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
- Share native drawing-stage preparation across text gradients and patterns;
  retain paint-server recording scopes independently of pattern-content selection.

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
