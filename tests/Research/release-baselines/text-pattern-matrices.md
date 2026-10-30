# Glyph-local pattern matrices

Text patterns retain the glyph drawing matrix separately from the shader-local
matrix before narrowing to floats, as linear and radial text paints already do.
The three paints share the same allocation-free rebasing calculation. Picture
texture sizes use the composed matrix's column lengths and ceiling; mapped vector
patterns always retain their image sampling matrices. The integer-period shortcut
is removed. Font layout, glyph coverage and source/object bounds are unchanged.

The pinned [picture shader](https://skia.googlesource.com/skia/+/f8b66b7597c4cc859d3ed190e9c6872241e6721c/src/shaders/SkPictureShader.cpp)
provides the texture sizing and image matrix conventions. Existing Skia notices
cover this adaptation. No fixed texture size, rounding epsilon or tolerance is added.

[The acceptance record](../text-paint-bounds/precision-pattern-images.json) retains
13 original candidate/control pairs covering spacing, negative width, trailing
text, mixed fonts, fill/stroke and outer opacity at output scale 1.5. All 26 cases
render individually through the existing canonical PNG path. Source and image
files are reused from the committed 39-input residual collection. Before this
correction, 4 matched on Windows and none on Linux; all 26 now match both hosts.
Every native pair is unequal and every case has different platform pixels.

Three healthy, byte-stable launches per host reproduce the original reference
PNGs exactly. Capture decodes the unmodified SVG through `Image`, then draws it
onto a transparent canvas sized to the authored dimensions times output scale,
with DPR 1. Only family names change to embedded ReviewFont and ReviewMono.
The regular/400 and Mono/700 faces use **two separate style elements**, preserving
the original graph. Tests reconstruct and check each native source hash as well
as font, source, reference, process and decoded pixel hashes. The
[regular](../supplied-font-labels/README.md) and
[Mono](../supplied-font-documents/README.md) licenses remain authoritative.

The full 960-input audit improves from 929 to 952 exact Windows results and from
926 to 955 on Linux, with zero exact losses or increased pixel error. Of the 39
retained originals, 28 are now exact on both hosts. Eleven remain unresolved:
eight Windows and five Linux results, with two shared. They are listed separately
from accepted rendering cases; two individually corrected candidates await their
companions' closure. One Windows result still has a one-pixel alpha difference;
the other residual differences affect color only. This closes the 13 named pattern
pairs, not the broader text, shader or decoration review.

[Eleven mechanism checks](../../Lokad.Svg.Tests/SvgTextPaintMatrixTests.cs) cover
reference/local ordering, singular maps, real curved-glyph float stages and both
sides of the texture ceiling boundary. One
[ownership interaction](../../Lokad.Svg.Tests/SvgTextNodeMetricsTests.cs) exercises
the resized glyph texture through raster admission refusal, cleanup, retry and
fresh prepared conversion after cancellation. Prepared documents remain single-use.
Existing cache, recording-chain and text ownership cases retain their responsibilities;
the destination matrix is not repeated for the new images.

Affected validation passes 14,544 core and 2,864 native/provenance checks on each
host/runtime combination, including the prior 855 text-bank cases and all 204
baseline declarations. Builds are warning-free. Single-run validation costs,
including test-host/discovery/TRX and excluding build:

| Host/runtime | Core | Native/provenance |
| --- | ---: | ---: |
| Windows .NET 11 | 33.37 s | 20.40 s |
| Linux .NET 11 | 30.73 s | 23.73 s |
| Windows .NET 10 | 42.89 s | 25.72 s |
| Linux .NET 10 | 37.87 s | 30.43 s |

These are validation costs, not renderer throughput measurements. The selection
is broader than the preceding baseline milestone, so its times are not a speedup
comparison. Full Windows/Linux CI and package gates qualify the milestone.
