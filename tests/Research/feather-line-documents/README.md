# Feather fractional-line documents

Three unchanged public MIT SVGs—Aperture, Feather and Sun—were retained from a
declared six-icon screen of [Feather v4.29.2](https://github.com/feathericons/feather/tree/1b002399e8758fb2bccea4d07312dc9e0f43dcb8/icons).
The original 2013–2023 Cole Bemis [MIT notice](LICENSE) and pinned source URLs/hashes
are retained. Activity, GitHub and Slack screened exact on both platforms and
were not added to the corpus. No success-rate claim follows from this small selection.

The `323dbf2c` baseline rejects these three documents because line endpoints are
parsed as integers. Replacing their lines with paths in ignored diagnostics makes
all three match their unchanged native screenshots, identifying an existing shared
stroke route rather than a need for another renderer.

The declared output is the original **24×24 transparent** image for each document,
without SVG edits, color overrides or resized recipes. Two authored CC0 controls
cover fractional axial endpoints with opaque/translucent/fractional-width strokes,
and source-definition replay with affine gradient and dashed square-cap placement.
Thirty healthy, identical fresh-process captures on Windows/Linux froze all five
inputs/options/references before correction. The [study](../../../artifacts/feather-line-documents-20261016/)
retains declarations, captures and diagnostics.

Finite bounded double endpoints now select the existing native-stroke path when
fractional. Integer fast paths remain selected as before; recording, markers and
source/shader replay consume the same typed endpoints with native float bounds.
Bare numbers/px and existing range/work limits remain in force; this does not
admit percentages or other endpoint units. All five outputs are exact on Windows/Linux.
Nine older fractional-line/opacity rejections now compare exactly with their unchanged
goldens; twenty-seven fresh Linux captures independently confirm those references.
Full local qualification passes **8,147 tests/platform**, **80s Windows / 252s Linux**
for build plus complete tests. Existing denial/cancellation/cleanup/retry coverage
includes Aperture and the source-replay control. [Terminal green CI](https://github.com/Lokad/Svg/actions/runs/38952070864)
qualifies exact source `e21f956c`, with all ten required checks/platform.
[PLAN](../../../PLAN.md) records completion and the separate pending private gate.

The three sourced documents are counted separately from the 36 authored documents,
the sourced W3C example, corporate **27/27 / 258/258** and selected **50/50**.
Controls add no document count. Established source/font/reference bytes and options
are preserved. Private deployment font qualification remains pending before adoption.
