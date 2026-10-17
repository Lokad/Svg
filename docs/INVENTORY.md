# SVG document collection and retained regression coverage

The current full-document collection is the [26-document upstream selection](../tests/Research/upstream-static-documents/README.md).
All declared outputs are exact against frozen Windows/Linux Chrome references. Each
input has a feature purpose, original source/license, one declared recipe and supplied
fonts where needed. [Finished Windows/Linux qualification](https://github.com/Lokad/Svg/actions/runs/39021816411) is complete for `47aa542e`. Document counts establish
only these selected outputs, not general SVG coverage.

## Required evidence

- [Native path/stroke bank](../tests/Research/native-path-replay/manifest.json): inherited/mixed paints, affine gradient maps, referenced rectangle joins and stroke coverage, separate from upstream membership. [Latest qualification](../artifacts/scope-gap-survey-20261017/qualified-results.json) covers 8,088 tests/platform and delivery checks; [prior ribbon qualification](../artifacts/path-paint-strokes-20261017/qualified-results.json) remains preserved.
- [Fixture-replacement qualification](../artifacts/full-fixture-replacement-20261017/standalone-qualified-results.json): 8,068 tests/platform, package/notices, installed consumers and terminal exact-source CI.
- [Selection, coverage and immutable asset manifest](../tests/Research/upstream-static-documents/manifest.json).
- [Rectangle source qualification](../artifacts/full-fixture-replacement-20261017/rectangle-source-qualified-results.json).
- [Rounded clip, nested viewport and PNG qualification](../artifacts/full-fixture-replacement-20261017/composition-qualified-results.json).
- [Filter/mask/clip qualification](../artifacts/full-fixture-replacement-20261017/effect-order-qualified-results.json): 523 core / 1,963 visual-resource checks per platform.
- A1-A6 focused completion banks for [pattern clips](../tests/Research/completion-pattern-clips/README.md), [object-box pattern clips](../tests/Research/completion-pattern-object-clips/README.md), [instance clips](../tests/Research/completion-instance-clips/README.md), [mask clips](../tests/Research/completion-mask-clips/README.md), [nested clips](../tests/Research/completion-nested-clips/README.md) and [filtered gradients](../tests/Research/completion-filtered-gradients/README.md) remain protected, alongside unchanged known-bug controls, API/resource/font-host contracts and seven-witness measurements.

## F1 retirement dispositions (2026-10-17)

The old whole logo/export/icon/authored-sheet membership and count/percentage gates
have left active document qualification. Their original source/reference bytes and
qualification remain available in the [pre-retirement inventory](https://github.com/Lokad/Svg/blob/284f8ec2/docs/INVENTORY.md).
Every old research document is listed below; a document with size/background variants
is one disposition. Upstream coverage replaces whole-document membership. The listed
focused controls retain the independently diagnosed behavior without an exhaustive
combination prerequisite.

### Corporate documents

The redundant mark/outlined-wordmark documents are `inv-001`, `inv-002`, `inv-003`, `inv-004`, `inv-005`, `inv-006`, `inv-007`, `inv-008`, `inv-009`, `inv-010`, `inv-011`, `inv-012`, `inv-019`, `inv-020`, `inv-026`, `inv-040`, `inv-066`, `inv-092`, `inv-095`, `inv-096`, `inv-097`, `inv-098`, `inv-099`, `inv-100`, `inv-101`, `inv-102`.
Their geometry, contour/stroke/paint and composed-map behavior is covered by the
upstream profile plus retained native-path-replay, source-convexity, affine-arcs,
clip and paint-recording micro-tests. The old 27/258 and 80% gates are retired.

`inv-027` retains one intended-size transparent native cubic-routing regression
and its existing native-picture/failed-model evidence. Its repeated size/background
document sweep is retired. The historical corporate manifest, source materializations
and reference bank remain inputs to the existing opt-in cohort benchmark/measurement
commands; ordinary CI no longer qualifies their full membership or percentage.

### Additional selected gap documents

Each row retires the original full output. The linked focused bank preserves the
relevant discovered semantics; upstream documents provide the replacement whole outputs.

| Retired document | Preserved focused behavior |
| --- | --- |
| `inv-103` | [pattern-vector-content](../tests/Research/pattern-vector-content/manifest.json) |
| `inv-104` | [pattern-inheritance](../tests/Research/pattern-inheritance/manifest.json) |
| `inv-105` | [transformed-rectangle-fills](../tests/Research/transformed-rectangle-fills/manifest.json) |
| `inv-106` | [iching-library](../tests/Research/iching-library/manifest.json) |
| `inv-107` | [gradient-masks](../tests/Research/gradient-masks/manifest.json) |
| `inv-127` | [curved-font-documents](../tests/Research/curved-font-documents/manifest.json) |
| `inv-128` | [pattern-general-placement](../tests/Research/pattern-general-placement/manifest.json) |
| `inv-131` | [object-bounding-clips](../tests/Research/object-bounding-clips/manifest.json) |
| `inv-130` | [transformed-rectangle-strokes](../tests/Research/transformed-rectangle-strokes/manifest.json) |
| `inv-132` | [affine-rectangle-strokes](../tests/Research/affine-rectangle-strokes/manifest.json) |
| `inv-133` | [affine-rectangle-strokes](../tests/Research/affine-rectangle-strokes/manifest.json) |
| `inv-134` | [affine-rectangle-strokes](../tests/Research/affine-rectangle-strokes/manifest.json) |
| `inv-135` | [affine-rectangle-strokes](../tests/Research/affine-rectangle-strokes/manifest.json) |
| `inv-136` | [compound-clip-content](../tests/Research/compound-clip-content/manifest.json) |
| `inv-137` | [open-clip-contours](../tests/Research/open-clip-contours/manifest.json) |
| `inv-138` | [pattern-viewbox](../tests/Research/pattern-viewbox/manifest.json) |
| `inv-139` | [pattern-instance-content](../tests/Research/pattern-instance-content/manifest.json) |
| `inv-140` | [gradient-stop-ranges](../tests/Research/gradient-stop-ranges/manifest.json) |
| `inv-141` | [group-mask-crop](../tests/Research/group-mask-crop/manifest.json) |
| `inv-142` | [vector-effects](../tests/Research/vector-effects/manifest.json) |
| `inv-143` | [filter-regions](../tests/Research/filter-regions/manifest.json) |
| `inv-144` | [filter-regions](../tests/Research/filter-regions/manifest.json) |

### Research full documents

| Historical bank and document(s) | Coverage disposition |
| --- | --- |
| [affine-arcs](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/affine-arcs/manifest.json): `tiger-viewport` | Unique behavior retained in [15 focused cases](../tests/Research/affine-arcs/manifest.json). |
| [affine-image-maps](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/affine-image-maps/manifest.json): `affine-image-map-sheet` | Unique behavior retained in [3 focused cases](../tests/Research/affine-image-maps/manifest.json). |
| [affine-oval-strokes](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/affine-oval-strokes/manifest.json): `affine-orbit-diagram` | Unique behavior retained in [6 focused cases](../tests/Research/affine-oval-strokes/manifest.json). |
| [affine-source-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/affine-source-exit-sheet/manifest.json): `affine-source-sheet` | Unique behavior retained in [4 focused cases](../tests/Research/affine-source-exit-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [bhutan-flag](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/bhutan-flag/manifest.json): `inv-064` | Redundant full coverage; focused banks: source-convexity, native-path-replay, affine-arcs and convex-path-clips. |
| [clip-union-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/clip-union-exit-sheet/manifest.json): `clip-union-exit-sheet` | Unique behavior retained in [5 focused cases](../tests/Research/clip-union-exit-sheet/manifest.json). |
| [colored-effects-markers](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/colored-effects-markers/manifest.json): `colored-signal-diagram` | Unique behavior retained in [12 focused cases](../tests/Research/colored-effects-markers/manifest.json). |
| [composite-flag-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/composite-flag-exit-sheet/manifest.json): `composite-flag-sheet` | Unique behavior retained in [3 focused cases](../tests/Research/composite-flag-exit-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [composition-resource-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/composition-resource-exit-sheet/manifest.json): `composition-resource-sheet` | Unique behavior retained in [3 focused cases](../tests/Research/composition-resource-exit-sheet/manifest.json). |
| [composition-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/composition-sheet/manifest.json): `composition-sheet` | Unique behavior retained in [1 focused cases](../tests/Research/composition-sheet/manifest.json). |
| [compound-clip-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/compound-clip-documents/manifest.json): `inv-119`, `inv-120` | Redundant full coverage; focused banks: compound-clip-content, clip-intersections and completion-nested-clips. |
| [cubic-viewport-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/cubic-viewport-exit-sheet/manifest.json): `cubic-viewport-sheet` | Unique behavior retained in [2 focused cases](../tests/Research/cubic-viewport-exit-sheet/manifest.json). |
| [curved-font-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/curved-font-documents/manifest.json): `inv-127` | Unique behavior retained in [5 focused cases](../tests/Research/curved-font-documents/manifest.json). Whole native reference retained for existing font-selection/provenance contracts; excluded from document enumeration. |
| [effects-marker-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/effects-marker-sheet/manifest.json): `effects-marker-sheet` | Unique behavior retained in [11 focused cases](../tests/Research/effects-marker-sheet/manifest.json). |
| [feather-line-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/feather-line-documents/manifest.json): `aperture`, `feather`, `sun` | Unique behavior retained in [2 focused cases](../tests/Research/feather-line-documents/manifest.json). |
| [filter-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/filter-documents/manifest.json): `original-logo-filter-mark`, `inv-125`, `inv-126` | Redundant full coverage; focused banks: filter-regions, filter-merges and completion-filtered-gradients. |
| [filter-owner-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/filter-owner-exit-sheet/manifest.json): `filter-owner-sheet` | Unique behavior retained in [13 focused cases](../tests/Research/filter-owner-exit-sheet/manifest.json). |
| [filter-source-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/filter-source-exit-sheet/manifest.json): `filter-source-sheet` | Unique behavior retained in [11 focused cases](../tests/Research/filter-source-exit-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [finite-support-exports](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/finite-support-exports/manifest.json): `inv-115`, `inv-116`, `inv-117`, `inv-118` | Redundant full coverage; focused banks: affine-arcs, pattern-vector-content, pattern-inheritance and mask-group-scopes. |
| [font-layout-diagram](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/font-layout-diagram/manifest.json): `font-layout-diagram` | Unique behavior retained in [4 focused cases](../tests/Research/font-layout-diagram/manifest.json). |
| [forward-bbox-clip-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/forward-bbox-clip-exit-sheet/manifest.json): `forward-bbox-sheet` | Unique behavior retained in [6 focused cases](../tests/Research/forward-bbox-clip-exit-sheet/manifest.json). |
| [glyph-resource-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/glyph-resource-exit-sheet/manifest.json): `glyph-resource-sheet` | Unique behavior retained in [3 focused cases](../tests/Research/glyph-resource-exit-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [instance-paint-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/instance-paint-exit-sheet/manifest.json): `instance-paint-sheet` | Unique behavior retained in [4 focused cases](../tests/Research/instance-paint-exit-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [linear-restoration-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/linear-restoration-exit-sheet/manifest.json): `mixed-consumer-single-blur` | Unique behavior retained in [3 focused cases](../tests/Research/linear-restoration-exit-sheet/manifest.json). |
| [malta-flag](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/malta-flag/manifest.json): `inv-057` | Redundant full coverage; focused banks: native-path-replay, source-convexity and curved-path-clips. |
| [marker-completion-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/marker-completion-sheet/manifest.json): `marker-completion-sheet` | Unique behavior retained in [6 focused cases](../tests/Research/marker-completion-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [marker-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/marker-documents/manifest.json): `inv-121`, `inv-122` | Redundant full coverage; focused banks: marker-content and marker-completion-sheet. |
| [marker-instance-content](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/marker-instance-content/manifest.json): `original-logo-marker-mark-64-transparent` | Unique behavior retained in [15 focused cases](../tests/Research/marker-instance-content/manifest.json). |
| [marker-presentation-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/marker-presentation-sheet/manifest.json): `marker-presentation-sheet` | Unique behavior retained in [2 focused cases](../tests/Research/marker-presentation-sheet/manifest.json). |
| [mask-color-affine-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/mask-color-affine-exit-sheet/manifest.json): `mask-color-affine-sheet` | Unique behavior retained in [3 focused cases](../tests/Research/mask-color-affine-exit-sheet/manifest.json). |
| [mask-group-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/mask-group-documents/manifest.json): `inv-145` | Unique behavior retained in [5 focused cases](../tests/Research/mask-group-documents/manifest.json). Whole native reference retained for existing font-selection/provenance contracts; excluded from document enumeration. |
| [mask-viewport-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/mask-viewport-exit-sheet/manifest.json): `mask-viewport-sheet` | Unique behavior retained in [5 focused cases](../tests/Research/mask-viewport-exit-sheet/manifest.json). |
| [offset-merge-document](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/offset-merge-document/manifest.json): `inv-146` | Unique behavior retained in [8 focused cases](../tests/Research/offset-merge-document/manifest.json). Whole native reference retained for existing font-selection/provenance contracts; excluded from document enumeration. |
| [path-marker-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/path-marker-documents/manifest.json): `inv-123`, `inv-124` | Redundant full coverage; focused banks: path-marker-content, marker-instance-content and completion-instance-clips. |
| [pattern-tile-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/pattern-tile-sheet/manifest.json): `pattern-tile-sheet` | Unique behavior retained in [7 focused cases](../tests/Research/pattern-tile-sheet/manifest.json). |
| [portugal-flag](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/portugal-flag/manifest.json): `inv-065` | Redundant full coverage; focused banks: convex-path-clips, curved-path-clips and clip-intersections. |
| [primitive-stroke-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/primitive-stroke-sheet/manifest.json): `primitive-stroke-sheet` | Unique behavior retained in [4 focused cases](../tests/Research/primitive-stroke-sheet/manifest.json). |
| [radiation-symbol](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/radiation-symbol/manifest.json): `inv-094` | Redundant full coverage; focused banks: oval-source-replay, curved-path-clips and native-path-replay. |
| [remaining-gap-exports](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/remaining-gap-exports/manifest.json): `inv-112`, `inv-113`, `inv-114` | Redundant full coverage; focused banks: paint-order, mask-content-order and mask-group-crop. |
| [remaining-label-path-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/remaining-label-path-sheet/manifest.json): `remaining-label-path-sheet` | Unique behavior retained in [8 focused cases](../tests/Research/remaining-label-path-sheet/manifest.json). |
| [remaining-scope-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/remaining-scope-documents/manifest.json): `inv-109`, `inv-110`, `inv-111` | Redundant full coverage; focused banks: pattern-vector-content, pattern-inheritance, pattern-general-placement and affine-arcs. |
| [retained-instance-effect-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/retained-instance-effect-sheet/manifest.json): `retained-instance-effect-sheet` | Unique behavior retained in [10 focused cases](../tests/Research/retained-instance-effect-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [root-resource-cascade](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/root-resource-cascade/manifest.json): `cascade-sheet` | Unique behavior retained in [4 focused cases](../tests/Research/root-resource-cascade/manifest.json). |
| [scoped-clips-masks](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/scoped-clips-masks/manifest.json): `scoped-card-diagram` | Unique behavior retained in [5 focused cases](../tests/Research/scoped-clips-masks/manifest.json). |
| [shared-map-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/shared-map-exit-sheet/manifest.json): `shared-map-exit-sheet` | Unique behavior retained in [5 focused cases](../tests/Research/shared-map-exit-sheet/manifest.json). |
| [shared-paint-map-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/shared-paint-map-sheet/manifest.json): `shared-paint-map-sheet` | Unique behavior retained in [3 focused cases](../tests/Research/shared-paint-map-sheet/manifest.json). |
| [south-africa-flag](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/south-africa-flag/manifest.json): `inv-061` | Redundant full coverage; focused banks: affine-rectangle-strokes, transformed-rectangle-strokes and vector-effects. |
| [static-css-document](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/static-css-document/manifest.json): `inv-129` | Unique behavior retained in [17 focused cases](../tests/Research/static-css-document/manifest.json). |
| [static-css-images](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/static-css-images/manifest.json): `css-image-card` | Unique behavior retained in [5 focused cases](../tests/Research/static-css-images/manifest.json). |
| [stroke-style-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/stroke-style-exit-sheet/manifest.json): `stroke-style-exit-sheet` | Unique behavior retained in [4 focused cases](../tests/Research/stroke-style-exit-sheet/manifest.json). |
| [stroked-decorations](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/stroked-decorations/manifest.json): `w3c-textdecoration01` | Unique behavior retained in [4 focused cases](../tests/Research/stroked-decorations/manifest.json). |
| [supplied-font-documents](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/supplied-font-documents/manifest.json): `commons-svg-translate-textpath` | Unique behavior retained in [7 focused cases](../tests/Research/supplied-font-documents/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [supplied-font-labels](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/supplied-font-labels/manifest.json): `original-logo-live-text` | Unique behavior retained in [7 focused cases](../tests/Research/supplied-font-labels/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [supplied-font-text-paths](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/supplied-font-text-paths/manifest.json): `original-logo-text-path` | Unique behavior retained in [7 focused cases](../tests/Research/supplied-font-text-paths/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [symbol-fractional-frames](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/symbol-fractional-frames/manifest.json): `symbol-card-diagram` | Unique behavior retained in [4 focused cases](../tests/Research/symbol-fractional-frames/manifest.json). |
| [symbol-presentation-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/symbol-presentation-sheet/manifest.json): `symbol-presentation-sheet` | Unique behavior retained in [2 focused cases](../tests/Research/symbol-presentation-sheet/manifest.json). |
| [text-map-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/text-map-exit-sheet/manifest.json): `text-map-sheet` | Unique behavior retained in [4 focused cases](../tests/Research/text-map-exit-sheet/manifest.json). Whole input/reference retained solely for existing font-selection/provenance contracts; excluded from document enumeration. |
| [tile-mask-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/tile-mask-exit-sheet/manifest.json): `tile-mask-sheet` | Unique behavior retained in [2 focused cases](../tests/Research/tile-mask-exit-sheet/manifest.json). |
| [viewport-clip-opacity-exit-sheet](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/viewport-clip-opacity-exit-sheet/manifest.json): `viewport-clip-opacity-sheet` | Unique behavior retained in [6 focused cases](../tests/Research/viewport-clip-opacity-exit-sheet/manifest.json). |
| [wikifoods-logo](https://github.com/Lokad/Svg/blob/284f8ec2/tests/Research/wikifoods-logo/manifest.json): `inv-108` | Redundant full coverage; focused banks: oval-source-replay, oval-percentage-lengths and native-clip-winding. |

The `inv-146` eight focused offset/merge/oval/text controls, all 25 I Ching
regressions and all 149 paint-recording results remain active. Public regular, bold,
real italic and fallback evidence is reused unchanged. Private deployment requests
remain pending before production adoption.

The four `inv-104`-`inv-107` SVG transports remain explicit core parser-test
inputs; their full-document visual membership and references are retired.

Unneeded retired output/source assets were removed; unchanged assets needed by
focused tests, font proofs, API/resource contracts, benchmark inputs or existing
optional measurement commands remain under those contracts. Historical bank README
details and provenance are available at the linked pre-retirement revision. No
reference tolerance, replacement pixel oracle or source-code fingerprint is introduced.
