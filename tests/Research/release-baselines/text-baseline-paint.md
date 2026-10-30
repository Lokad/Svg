# Text-path baseline painting

Middle and hanging baselines keep separate layout and paint metrics. Layout
retains its fractional 1/64-unit offset; path painting uses integer metrics at the
recorded font scale before applying the remaining transform. These conventions
follow the [pinned font metrics](https://chromium.googlesource.com/chromium/src/+/refs/tags/153.0.8010.52/third_party/blink/renderer/platform/fonts/font_metrics.cc).
Ordinary text retains its existing route. Authored `dy` offsets remain fractional.

[Baseline metrics](../../../src/Lokad.Svg/Parsing/SvgTextBaselineMetrics.cs)
represent both values explicitly. Path placement saves the original semantic
source map before applying the paint adjustment. Shaping, advances, fitting,
source/object boxes and operation order retain their existing calculations;
glyphs and path decorations consume the paint placement together.

The [acceptance map](../text-node-boundaries/path-baseline-acceptance.json) retains:

- 120 original R108 declarations: four patterned-path residuals at scale 1.5
  and 116 nested-viewport/scaling inputs at scale 1.125.
- 84 independent diagnostics: solid/linear/pattern paint; direct, straight and
  curved text; middle, hanging and alphabetic baselines; scales 1 and 1.5.
- Every original source, role, font/capture binding and independent platform image.
  Eight exact source/native-context/options identities share canonical cases;
  all 204 declarations remain mapped to 196 individual rendering cases.

All 102 native pairs remain independent, including 44 unequal pairs per host.
Every declaration has different Windows/Linux pixels. Three healthy, byte-stable
launches per host bind each reference; historical captures agree wherever available.
The original regular and unused Mono capture bindings are preserved, with the
existing [regular](../supplied-font-labels/README.md) and
[Mono](../supplied-font-documents/README.md) font licenses.

Before correction, 179 of these 204 declarations matched each host's reference.
All 204 now match exactly under both supported runtimes, with zero exact losses.
The four original residuals, straight-path scaling failure and hanging-path
failures close together through the shared metric distinction. This does not
close the broader R48/R49/R60/R61 or R103/R104 precision review.
[Thirty-nine remaining paint/fitting witnesses](../text-paint-bounds/precision-residuals.json)
retain their original sources, options and independent platform images for that review.

[Fourteen mechanism vectors](../../Lokad.Svg.Tests/OpenTypeFontLineMetricsTests.cs)
exercise fractional layout versus recorded paint metrics and integer boundaries
directly. The existing canonical paint path covers each rendering case; separate
contracts retain preparation, font ownership, refusal, cancellation and retry.
The [integrity checks](../../Lokad.Svg.VisualTests/SvgOriginalTextAcceptanceTests.cs)
verify mapping, both platforms, native context, font hashes and unequal companions.
The bank's previous 659 cases and all existing references remain unchanged.

Affected validation passes 13,111 core and 2,376 native/provenance checks on each
Windows/Linux and runtime combination. Warning-free builds and exact comparisons
precede promotion. Single-run validation costs, including host/discovery/TRX and
excluding build:

| Host/runtime | Core | Native/provenance |
| --- | ---: | ---: |
| Windows .NET 11 | 39.23 s | 20.97 s |
| Linux .NET 11 | 33.73 s | 24.83 s |
| Windows .NET 10 | 38.43 s | 19.29 s |
| Linux .NET 10 | 26.51 s | 23.78 s |

These are validation costs, not renderer throughput measurements.
