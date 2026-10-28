# Changelog

## Unreleased

- Share source bounds and instance maps between point-list geometry and its
  linear/radial paints. Retain symbol viewport clips through native viewBox
  concatenation and output placement, correcting large-coordinate clipping
  without widening numerical admission. See the
  [native controls](tests/Research/point-list-source-maps/README.md).

- Preserve requested source-image bounds independently of capture storage, and
  retain image operating spaces through offset aliases. Merge draws transfer
  sampled color before coverage and use their destination clip. This corrects
  affine, fractional and supplied-font filter sampling through resource replay.
  Recorded sources share their owning layer map, preserving ellipse and curve
  coverage without shape-specific matrix cancellation.
  See the [native controls](tests/Research/filter-merge-image-draw/README.md).

- Preserve packed filter-image coverage and enclosing opacity-layer bounds,
  applying opacity after sampling and crop coverage. Share shadow-tint and offset
  padding arithmetic. Retain near-integer sprite behavior and independent
  compact/expanded shadows through use, symbol and pattern replay. See the
  [native controls](tests/Research/filter-opacity-sampling/README.md).

- Preserve filter requests and pending color/sampling through crops and aliases.
  Keep captured source, child output and stored-image extents distinct, retaining
  shadow fringes and scaled/nested composition. Enforce total raster work before
  returning storage. See the
  [native controls](tests/Research/filter-stage-boundaries/README.md).

- Retain terminal offsets of materialized filter images, propagate stored offset
  and merge extents, and preserve source-alpha shader rounding before resampling.
  See the [native controls](tests/Research/filter-stored-images/README.md).

- Enclose filter source hints before layer scaling and select image edge coverage
  using the actual parent clip. Preserve translated alpha, near-integer offsets
  and reflected placement. See the [native controls](tests/Research/filter-image-placement/README.md).

- Retain source-alpha shader precision through zero blur, one-node merge and
  zero-offset aliases during affine image sampling, including backdrop, use and
  pattern replay. See the [native controls](tests/Research/filter-alpha-stages/README.md).

- Quantize compact shadow tint and opacity before coverage, retaining float
  precision for pending linear-RGB color conversion and packed lanes for blurred
  images. See the [native controls](tests/Research/filter-shadow-stages/README.md).

- Preserve affine and fractional filter image bounds, crop coverage and source
  capture across compositor bitmaps, including negative offsets, reflections and
  offset graphs. See the [native controls](tests/Research/filter-affine-restoration/README.md).

- Preserve filter restoration precision across linear-RGB/sRGB transfers, opacity,
  offset and merge aliases, pattern images, and clipped nontransparent backdrops.

- Preserve ancestor clip ownership around child filters, including forward and
  object-bounding rectangles, retained use and pattern scenes. Keep source-only
  clips and per-bitmap filter requests separate.

- Keep large vector patterns on one raster device so gradients retain their
  native phase across compositor seams.

- Preserve fractional filter source coverage through cropped aliases and small
  blur materialization, including native deviation parsing and image-hint rounding.

- Preserve native text filter image origins through stroke overrides, hidden text,
  reflection and resource replay, with separate recording and element bounds.

- Share shaping, source cursors and object paint bounds across mixed direct/path
  text; preserve authored closed endpoints and inert empty paths. Retain queued
  own-filter wrappers with bounded source-local command remapping.

- Preserve authored text-path positioning boundaries while ignoring absolute Y
  movement, and use measured scalar path points for horizontal glyph paint.

- Retain unpainted text-path characters in shared gradient bounds, including
  hidden spans and resource replay.
- Round final anchored text rectangles and skip fitting-range work when no
  fitting was requested.
- Preserve text-node fragment widths and ligature-internal relative offsets through
  fitting and resource replay, including nodes that own no painted glyphs.
- Document text keyword, positive font-size and clip/mask declaration boundaries,
  with direct and prepared admission checks.
- Preserve filter input consumers through offset and zero-blur aliases.
- Keep `SourceAlpha` rounding consistent across merge execution paths.
- Preserve packed image rounding in offset/merge graphs while retaining shader inputs.
- Include decoration ink beyond text drawing bounds when sizing viewport clip surfaces.
- Retain constant-mask regions when computing enclosing paint bounds.
- Share native text drawing bounds with decorations and preserve scalar path source positions.
- Honor isolated layer bounds when painting rectangles.
- Preserve horizontal text-path paint origins before affine placement.
- Preserve recorded font sizes when replaying mapped glyphs.
- Render scaled and affine text paths with horizontal tangents.
- Retain enclosing filter source bounds through deferred text and instance replay.
- Preserve fractional `textLength` widths when measuring text-path paint boxes.
- Include letter/word spacing in text-path character widths before rounding,
  preserving signed advances and spacing boundaries.
- Measure text-path paint boxes from original coordinates before glyph paint truncation.
- Enclose text drawing bounds before output mapping to preserve gradient colors
  under group opacity; reuse fitted metrics across parser runs and share paint records.
- Include fitted glyph ink in text paint object boxes, preserving source fragment
  coordinates, anchors and fitted spacing before paint-origin truncation.
- Account for retained viewport context fields and their reservation token
  before allocating parser storage.
- Preserve nested viewport boundaries when applying outer opacity to shader
  paints, while retaining single-paint opacity inside the viewport.
- Preserve pattern sampling across clip, mask and opacity recording boundaries,
  including instances, supplied-font text and affine maps. Keep authored viewBox
  transforms distinct from external output scaling.
- Keep inherited and own rectangular clips outside mask-owned layers at full
  group opacity, preserving single-paint colors and gradient bitmap grids.
- Keep a group's own rectangular clip outside its partial-opacity layer in both
  reference orders. Preserve bounded output work for large transformed axis clips.
- Apply inherited viewport clips when restoring isolated opacity groups, keeping
  local child clips and scalar maps. Preserve forward rectangular clip ordering
  and rebuild sibling command scopes once per scene.
- Avoid extra isolated surfaces for clips containing the complete destination,
  preserving shader origins inside child masks.
- Replay thin gradient and pattern strokes through native hairline coverage,
  preserving material alpha and ordered corner/curve paints. Share polygon
  shader sampling without per-pixel callbacks or closure allocations.
- Preserve the path start and drawing bounds of rectangles with one zero corner
  radius, and compose retained linear-gradient maps through symbol placement.
- Preserve native rectangle fill coverage beside solid and shader strokes, and
  round retained rectangle drawing bounds before mapping isolated opacity layers.
- Share rectangle lowering across direct and retained sources, preserving native
  shader-stroke coverage through fractional placement, widths and output scaling.
  Remove obsolete definition helpers and retain clip lookup until replay.
- Skip pattern fonts for intrinsically hidden primitives and fill-only lines or
  two-vertex contours; preserve stroke, filter and mask dependencies and bounded parsing.
- Reconstruct named shader sources hidden by an ancestor when a visible instance
  uses them, preserving the source's own visibility declarations.
- Skip pattern tile fonts for primitive channels with explicitly zero fill/stroke
  opacity or stroke width, preserving live paints and instance inheritance.
- Preserve mask paint clips through fractional instance placement and move
  explicit regions with uniform masks, keeping native curved-edge coverage.
- Paint markers on unstroked line instances and resource hosts, resolve forward
  marker definitions, and retain solid marker corners for later affine instance maps.
- Resolve gradient and pattern paints declared after marker content and its host,
  preserving inherited fills on deferred shapes and paint ownership in pattern tiles.
- Skip pattern tile fonts for declared empty primitives and paths. Preserve empty
  symbol contours and whitespace-only paths through replay, accounting for mapped
  point and curve scratch storage before allocation.
- Resolve local instance fills and strokes when their paint resource is declared
  later, including paints authored on the source, use or enclosing container.
- Skip fonts and glyph construction for text with unavailable paint resources,
  preserving span advances, independent paints and enclosing filter bounds.
- Honor display on definitions and paint resources, preserving local template
  replacements and ordinary use sources. Skip unavailable mask and marker fonts.
- Resolve explicit display inheritance through containers, local instances and
  resource children, keeping ancestor suppression and font discovery consistent.
- Preserve antialiased region edges when restoring affine terminal flood filters,
  including empty symbol sources; enforce the restoration path's complete work charge.
- Let authored pattern child elements override inherited content even when they
  paint nothing; keep childless and text-only aliases inheriting. Avoid fonts in
  unreferenced or overridden pattern sources while retaining reference validation.
- Skip mask fonts for empty group sources and hidden rectangle children, preserving their own filters
  and suppressing empty descendant-filter output. Retain empty replay slots as
  effect anchors rather than artificial geometry bounds.
- Resolve mask references on self-closing empty groups in either source order
  without opening mask fonts; preserve filters that generate output.
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
