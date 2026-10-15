# Clip union exit sheet

This bank freezes one complete authored CC0 document, four independent C1
controls and the unchanged MIT 43-child input from
[SvgCompoundClipAdmissionTests](../../Lokad.Svg.Tests/SvgCompoundClipAdmissionTests.cs).
Current Windows/Linux measurements are **0/1 complete document / 1/5 exact
controls**, from admission **0/1 / 0/5** at declaration revision `bf114d81`.
The mixed rectangle/path control is exact; curved children, compound/cascade
content and both larger-child fallback controls still reject. These remain
required exits in [PLAN](../../../PLAN.md).

Basic rectangles retain their source box/map without allocating a path until a
sibling requires union. The shared straight-boundary resolver splits crossing
edges, removes interior spans and retains native float endpoints/closed replay.
It removes the initial 160-pixel edge difference on both platforms. Independent
native boundary vectors and the existing denial/cancellation/cleanup/retry checks
protect the correction; the whole sheet is not complete.
The [receipt](../../../artifacts/maintenance-review-20261012/clip-crossings-qualified-results.json)
records full qualification, prior-result preservation and the remaining exits.

The sheet joins mixed rectangle/path content, overlapping curved children,
compound/concave children with independent winding, and the larger child-count
fallback. The compound control also exercises inherited clip-rule and child
override with stylesheet/inline priority, shared with C5. Its first measured gate
is clipPath container style admission; later gates must be measured after that
gate is removed. The original Core input separately protects the established
42/43 transition and original source semantics.

The [manifest](manifest.json) fixes original SVG/PNG bytes, dimensions, scale one,
transparent background and default resource limits. The existing canonical visual
test retains the exact result and each current categorized rejection. Remove an expected rejection
only when that unchanged case becomes exact against both platform references.

The [study](../../../artifacts/clip-union-exits-20261015/) retains the declaration,
both baseline runs and thirty-six healthy stable fresh-process captures with
successful profile cleanup. The first native PNG from each platform is retained
unchanged. Asset hashes protect frozen inputs/references; there are no renderer,
source-code, build or report fingerprints.

Keep native path unions distinct from the larger-child mask replay. The
[Blink clipper](https://chromium.googlesource.com/chromium/src/+/refs/tags/153.0.8010.52/third_party/blink/renderer/core/layout/svg/layout_svg_resource_clipper.cc)
and [matched Skia union builder](https://skia.googlesource.com/skia/+/f8b66b7597c4cc859d3ed190e9c6872241e6721c/src/pathops/SkOpBuilder.cpp)
explain that distinction; frozen platform pixels remain the acceptance authority.
Reuse existing geometry/source replay, bounded allocation/work, cancellation and
cleanup. C1 remains open until the whole sheet and all five controls are exact;
C2–C4 and the other applicable C5 resource scopes remain required independently.
The sixteen completed authored documents and frozen 27/258 cohort stay unchanged.
