# Upstream static SVG documents (F1)

This finite selection is frozen before fresh renderer comparisons. It replaces
full logo/export/icon/authored-sheet membership after qualification and coverage
disposition; existing micro-tests, known-bug, API/resource and font-host contracts
stay. All selected outputs are exact on both platforms. The
[test guide](../../README.md) defines admission and qualification.

The [manifest](manifest.json) records 26 unchanged documents, original hashes,
pinned upstream identity/license, feature purpose, one viewport/scale/background
recipe and the exact supplied font programs. Chrome 153.0.8010.52 is the sole
pixel oracle. Upstream expected PNGs and pass tables are unused.

Selection reviewed these pinned inventories:

- [resvg-test-suite](https://github.com/linebender/resvg-test-suite/tree/d8e064337faf01bc5a9579187a56dbdbe3eacc72),
  commit `d8e064337faf01bc5a9579187a56dbdbe3eacc72`: shapes (133 SVGs),
  painting (304), paint servers (149), masking (93), structure (247), filters
  (397), text (356). Selected 24 documents by feature, without repetitive variants.
- [WPT SVG](https://github.com/web-platform-tests/wpt/tree/df642c4142354ffb5a0af6fa41610677f6321092/svg),
  commit `df642c4142354ffb5a0af6fa41610677f6321092`: complete SVG subtree inventory
  (2,517 entries, not truncated). Two standalone marker/viewport documents add
  interactions. HTML/DOM/script/animation, external-resource and unsupported
  feature tests are outside this static profile. Duplicate reference documents
  are not fixtures. Inspected `line-dasharray.svg` overlaps the selected curved
  dash document and retained `SvgRootParserTests`/`SvgStrokeFillTests` pathLength
  controls; pathLength remains admitted. `clip-path-filter-order.svg` requires
  excluded feColorMatrix and is not selected.
- [Accepted W3C SVG 1.1 F2 index](https://dev.w3.org/SVG/profiles/1.1F2/test/harness/htmlObjectApproved/index.html),
  retained 2026-10-17 with SHA256
  `365f7d07615f2053ac0da65ba2f97d8281b01077b785dad87584d49ebe81d8af`.
  Reviewed accepted `shapes-rect-01-t`, `paths-data-01-t` and `coords-trans-01-b`:
  their original harness uses external SVGFreeSans SVG-font definitions, outside
  our standalone TrueType boundary. Removing/replacing that behavior would not
  be an unchanged fixture. Geometry/maps are represented by resvg/WPT; no W3C
  harness document is selected. This makes no claim about every accepted test.

## Coverage

| Documents | Required feature or interaction |
| --- | --- |
| geometry-rect, geometry-cubic, geometry-arc | Closed geometry, cubic/smooth and elliptical path strokes |
| stroke-dashed-circle, stroke-round-join | Curved dashes and round joins |
| nested-affine, symbol-opacity, wpt-nested-viewport | Composed transforms, retained instance opacity and physical viewport |
| linear-affine, radial-affine | Paint and target maps |
| object-box-clip, nested-clip-child, clip-child-union | Object bounds, nested child intersections and unions |
| luminance-mask, filter-mask-clip | Gradient luminance/opacity and clip/mask/filter ordering |
| pattern-viewbox, pattern-affine | Bounded tile content, viewBox and composed phase |
| embedded-png, css-priority | Embedded PNG placement and static CSS priority |
| blur, blend-multiply, composite-in, merge-color-spaces | Bounded captures, finite binary effects, named results and mixed spaces |
| wpt-marker-path | Auto path markers and inherited styles |
| text-real-italic, text-path | Intended supplied real italic and curved text layout |

The existing A1-A6 micro-banks retain clipped pattern content, filtered gradients
and retained instance/marker clips. Other focused banks retain filtered instances,
font fallback/metrics, all admitted primitives, caps/joins/units, negative and
resource outcomes. They complement this finite document selection; no exhaustive
combination matrix is a completion condition.

## Original assets and capture transport

`inputs/` and `fonts/` are byte-for-byte upstream assets; notices are under
[notices](notices/). Their original sources stay optional and unreferenced in
`external/`. Offline tests need no downloads. Fonts are the suite's supplied
regular and real italic Noto Sans under OFL-1.1, without machine-font discovery.

`capture-inputs/` records only explicit root sizing for the declared viewport
and, on the two text documents, appended data-URI @font-face bindings for the
unchanged requested family/style. Every other original XML node/attribute is
preserved. Lokad.Svg receives the original input plus the same viewport/fonts
through its existing API/catalog/provider. All 174 fresh-process Windows/Linux captures have healthy startup, successful
cleanup and identical triples. Intended text differs from absent-font controls
on both platforms; real italic also differs from regular-font substitution.
Their references, hashes and provenance are retained in the manifest.

## Verification

The [manifest](manifest.json) declares expected outcomes, original input/font
hashes and platform references. [SvgUpstreamDocumentTests](../../Lokad.Svg.VisualTests/SvgUpstreamDocumentTests.cs)
verifies those contracts and exact rendering. All 26 declared outputs are exact
on Windows/Linux; focused effect/viewport/PNG/rectangle controls retain diagnosed
behavior independently. See [retired collections](../../README.md#retired-collections)
for the replacement of earlier full-document sweeps.
