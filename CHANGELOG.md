# Changelog

## Unreleased

- Skip mask font construction for nonpainting primitive targets and disabled
  groups or instances, retaining visible descendants, effect output and validation.
- Treat zero-sized symbol and nested SVG viewports as empty before lowering
  children or acquiring their fonts; preserve ordinary use target dimensions
  and parent filters that generate paint from empty references.
- Discover fonts used only by displayed mask content through groups and local
  references, using the resource's inherited style during SVG-driven preparation.
- Preserve inherited text style when replaying grouped mask content, keeping
  the resource's font and spacing independent of the masked target.
- Defer mask scenes until target placement, skipping unused mask text and
  explicitly empty regions. Share retained replay for single-use and grouped masks.
- Admit inert `title`, `desc` and `metadata` children inside text, including
  retained instances, without spending the rendered-text character allowance.
- Preserve fractional glyph coverage through symbol viewport clips, including
  nested opacity, and replay deep viewport chains without recursive clip calls.
- Preserve native affine image edge coverage inside symbol viewport clips,
  including faint and nearly opaque fringes across bitmap and layer opacity.
- Preserve owning use clips through x/y placement, forward references and real
  instance bounds. Keep clip coverage outside restored opacity/mask output and
  preserve single-paint opacity through pure clip scopes.
- Keep uniform images as compact bitmap paints, preserving alpha and color
  rounding through groups and local instances.
- Preserve authored polygon/polyline fill coverage through fractional placement,
  shader paints, instances and output scaling.
- Share native pattern content for fills and strokes, preserving fractional
  sampling and nested-viewport replay for single-rectangle tiles.

- Avoid spending the work budget on Linux glyph fitting outside the raster target.

- Categorize nonfinite native transform coordinates before affine construction,
  retaining the offending SVG element and attribute in source diagnostics.

- Size strokeWidth markers against non-scaling strokes through instances and
  viewports, including symbol viewBox maps. Preserve native drawing-bound clamping
  and finite inverse placement; culled drawings retain object bounds and filters
  that generate content independently of their source.
- Ship a self-contained package README and validate its local link targets in the
  actual archive before the installed-consumer smoke check.
- Preserve the original rectangular clip when a sibling rectangle is contained
  within it, retaining fractional coverage through instances and resources.
  Bind retained-group object-box clips after replay supplies their geometry bounds.
- Anchor text to its positioned advance extents, including relative shifts and
  backtracking; preserve separate native fragment-origin and anchor rounding.
- Preserve independent fill, stroke, markers and effects when a gradient or
  pattern paints nothing, including masks, local instances and supplied text.
  Keep empty text strokes out of native layer bounds and defer forward mask paints.
- Preserve original gradient-template coordinates and replace local affine maps
  once through shape/text paints and resources. Keep explicitly inherited radial
  focal fields when replacing center/radius; share immutable definition storage.
- Decode local UTF-8 URI fragments once across paint/effect resources, templates,
  text paths and font discovery, avoiding accidental binding to literal encoded IDs.
- Preserve escaped gradient hrefs through deferred registration so forward targets
  select the same identifiers as earlier definitions.
- Check authored filter lengths and marker orientations before trimming, including
  orientation keywords; preserve the existing scalar-token limit and cleanup on rejection.
- Preserve source-independent filters on paintless geometry and empty local uses,
  with real source bounds for ancestor effects. Keep suppressed leaves out of
  those bounds and preserve filter, mask, clip and opacity order through instances
  and resource content. See the [filter-source regressions](tests/Lokad.Svg.Tests/OpenTypeFontTests.PaintlessFilterSources.cs).
- Add ordered CSS font-family lists through `SvgFontFaceRequest.FromCss` and
  explicit host generic/fallback mappings through `SvgFontSelectionPolicy`.
  Preserve quoted names, escapes, inheritance and bounded canonical round trips.
- Acquire supplied-font fallbacks lazily for missing admitted scalars, preserving
  mixed-face layout, kerning, ligatures, spans, text paths and failure diagnostics.
- Extend bounded static TrueType hinting and composite support. Honor selected
  fitting modes, metric origins and Windows/Linux raster profiles, with improved
  conic, thin-stroke, dropout, stem and fractional-size glyph fidelity.
- Support applicable pair-positioning lookup flags and GDEF base/ligature filters.
  Prefer selected GPOS kerning, including zero adjustments, over legacy kern data;
  actual mark positioning remains unsupported.
- Reserve embedded-PNG growth and scanline scratch before allocation; improve
  temporary-storage cleanup, cancellation and failure classification. Document
  the 135-device-pixel drop-shadow deviation limit and report it directly.
- Validate embedded PNG chunk structure and ordering, and reject malformed input
  or unsupported transparency in the independent pixel comparer. Reject escaped
  FontProbe pack traversal before requests; make its shared HTTP cache
  cancellation-aware and thread-safe.
- Reduce repeated CSS, geometry and PNG sizing work; keep coincident stroke
  lookahead and catalog fallback deduplication linear. Share document preparation
  and polygon traversal, with more compact retained geometry.
- Treat empty stylesheets as no-ops, bound stylesheet discard storage, and avoid
  repeated pattern-template graph walks across defs blocks and later shapes;
  wake aliases only when their template dependencies resolve.
- Resolve gradient template chains without order-dependent rescans and reject
  duplicate IDs consistently across linear, radial and forward definitions.
- Keep polygon clips compatible with unrelated group instances, and expand
  forward uses without repeated command/range shifts.
- Batch deferred paint, text and pattern-use commands while preserving original
  scope boundaries and scopes created during replay. Keep retained text remapping
  local to its source, independent of unrelated document ranges.
- Index pattern group boundaries once and compact clip-only markers together;
  retain separate sibling masks and filtered leaf boundaries through expansion.
- Simplify renderer/tool internals and diagnostic reporting, validate downloaded
  CI artifacts against their recorded host, and retire historical corporate-sweep
  tooling and aggregate reports while retaining focused evidence and provenance.

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
- Size/work limits, approximate memory accounting, cancellation, deterministic
  cleanup and documented borrowed-resource lifetimes.
- Frozen Windows/Linux visual references, public font checks and installed-package
  consumer verification. Packages include the icon, README, changelog, licenses,
  notices and a separate portable-PDB symbol package.
- NuGet output under `artifacts/nuget/`, with non-Release packaging rejected by
  default.

See the [support matrix](docs/SUPPORT_MATRIX.md) for the implemented static SVG
subset, font/shaping restrictions and explicit unsupported features.
