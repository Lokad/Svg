# Hosted SVGWeb review

[Originals](originals.json) accounts for all 204 direct `.svg` entries in the
[hosted directory](https://dev.w3.org/SVG/tools/svgweb/samples/svg-files/): URL,
source hash/size, declarations, lexical font/resource use and platform outcomes.
Fetch bodies into bounded memory and discard them. Ordinary tests are offline;
this directory contains metadata and original orthogonal controls.

The current default conversion renders 148 originals per host: **139 exploratory
exact and 9 different**, with 53 UnsupportedFeature and 3 MalformedInput outcomes.
These observations use transparent output without supplied fonts/resources.
Scimitar is exact; Gallardo differs at 40 pixels/46 total channel error and UFO at
30 pixels/63 total channel error. See `currentReview` in the inventory for all nine
remaining differences. Exact default output does not qualify other font, resource
or static-state contexts, and whole-corpus acceptance remains open.

Resolve gaps by mechanism, preserving original-to-control mappings and independent
references. A successful render, ambient font substitution or equal images alone
cannot establish correctness or redundancy. Font declarations are lexical inventory,
not resolved requests; navigation is separate from image/CSS/media dependencies.

## Validation responsibilities

- Mechanisms pin numerical stages, authored geometry, parsed lengths, bounds and
  rounding directly. Keep meaningful boundary coverage in these tests.
- Representative ownership tests protect refusal, cancellation, cleanup and retry.
  Preserve distinct allocation paths without repeating them for every image.
- Rendering controls normally use canonical conversion. Reference admission requires
  three fresh healthy, profile-cleaned captures on each platform and exact existing
  comparisons. Preserve meaningful platform differences and failing-before cases.
- Recheck all original hashes, outcomes, diagnostics, dimensions and rendered output
  after shared fixes. Hosted bodies and fragments remain in memory only.

The [capture model](capture-model.json) distinguishes image/Blob, background and
viewport contexts. Match logical image viewport separately from screenshot size;
label static-state changes and resource materialization. Scripts are inactive in
image documents; animation, ambient fonts and unresolved resources need explicit
qualification. The [tool procedure](../../../tools/README.md#hosted-svgweb-inventory)
reproduces the inventory without retaining SVG files.

## Shared rendering model and evidence

Each record links mechanisms, original controls, native provenance and scoped
acceptance. Measurements and rejected experiments remain in the records; the
current inventory above governs overall status.

| Responsibility | Evidence and retained contracts |
| --- | --- |
| Root image layout and placement | [Root model](root-image-model.json), [projection stages](root-image-projection.json), [image boundary](root-image-boundary.json). Logical sizing, subpixel layout, recorded scene and destination coefficients stay distinct. Direct matrices and large viewBox controls protect stages that small images cannot distinguish. |
| Root experiments | [Viewport review](root-viewport-review.json), [stage review](root-image-stages.json). Preserve rejected output-only stretching and context distinctions. |
| Filter images and restoration | [Image model](filter-image-model.json), [bitmap restoration](filter-bitmap-restoration.json), [Gaussian aliases](gaussian-alias-restoration.json). Retain deferred/color-filter state, cropped storage, padding, enclosing opacity and final sampling roles. |
| Filter source requests | [Source capture](filter-source-capture.json), [request propagation](filter-source-request.json), [bitmap clip requests](filter-bitmap-clip-request.json). Ordinary, nested and image-containing sources use propagated requests; each bordered destination bitmap uses its own nonzero clip bounds. |
| Parameter source enclosure | [Capture and output hints](filter-parameter-source-enclosure.json). Enclose source visuals before graph expansion and the result before device mapping. Ordinary and affine capture share the parameter stage; recorded paint hints and captured content remain distinct. |
| Source raster windows | [Clip construction](source-clip-construction.json), [raster window](filter-raster-window.json). Admitted numerical paint and construction padding remain separate from tight visual extents and captured content. |
| Nested pictures and affine capture | [Nested picture](filter-nested-picture.json), [transform review](filter-transform-review.json). Keep original descendant paint hints, filtered output and parameter capture enclosure distinct. Both opposed affine controls are exact canonical regressions. |
| Filter numerical stages | [Black linear stages](black-linear-stages.json), [bilinear weights](filter-bilinear-review.json), [numeric vectors](../../Lokad.Svg.Tests/TestData/highp-bilinear.json), [browser probe](highp-bilinear.js). Transfer, byte storage, packed alpha, high-precision weights and addressing have separate responsibilities. |
| Clip source geometry | [Referenced paths](clip-use-construction.json), [source placement](clip-source-placement.json), [canvas boundaries](clip-canvas-boundaries.json). Retain authored quadratic/cubic/arc operands, local resource placement and referencing-canvas concatenation. |
| Clip coverage and bounds | [Replay state](clip-replay-state.json), [visual bounds](clip-visual-bounds.json), [analytic alpha](analytic-alpha-bytes.json). Separate silhouette coverage, stroke/layout enclosures, numerical raster ink and native byte narrowing. |
| Radial paint binding | [Source binding](radial-source-binding.json). Keep local gradient coordinates and reference placement through axis/skew, clipping and inherited stops. |
| Gradient declarations | [Typed lengths](gradient-lengths.json), [inheritance fixture](../../Fixtures/original/radial-coordinate-inheritance.svg), [stop identity fixture](../../Fixtures/original/gradient-stop-identities.svg). Preserve number/percentage intent, missing/default values, units, per-field inheritance, viewport resolution and metadata. |
| Paint selection and text evidence | [Paint references](paint-references.json), [filter-source review](filter-source-review.json). Empty paint content and unavailable servers differ; the synthetic font needs valid bounds before its rendering can diagnose the library. Exploratory single captures do not admit new references. |

## Original acceptance mappings

The [280-leaf Gallardo map](gallardo-paint-map.csv) retains 58 native paint isolations
and the [referenced-clip record](clip-use-construction.json) maps 35 filter consumers.
The current [bitmap request record](filter-bitmap-clip-request.json) rechecks both
collections. The clipped `path3850` blur and both captured-gradient consumers
(`use4125`, `use4171`) are exact;
the complete Gallardo image remains unresolved. The
[Scimitar scope map](scimitar-scope-map.csv) preserves all 43 scopes; the
[inner-filter map](scimitar-inner-filter-map.csv) preserves 20 companions, including
the complete 35-child shadow. These and the complete Scimitar original remain exact.

Remaining work includes filter crop/flood-offset/shadow interactions, declared
stroke units, CSS and gradient boundaries, explicit font programs, static-state
traversal and host-bound raster resources. Follow actual parent/traversal state
before attributing a rejection to its reported child. Keep malformed, unsupported
and font/resource-dependent cases individually classified; removing one blocker
can expose another.
