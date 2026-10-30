# Shared paint drawing stages

Text gradients and picture patterns use one native drawing-matrix preparation
path. It retains external output, authored root, recorded scopes and source
placement before shader-local composition. Gradient sampling accepts the prepared
`SvgShaderMatrix` directly. Three forwarding overloads and a duplicate narrowing
helper are removed. Source/layout bounds and the distinct native matrix arithmetic
remain unchanged; this adds no coordinate framework, renderer or retained owner.

Group, instance and symbol recording respond to potential paint-server references.
Glyph emission retains those stages for any shader. Recording no longer depends
on the presence of selected pattern content. The existing resolved consumer index
still selects resource content and fonts; the conservative metadata flag performs
no acquisition. Shared recording preserves cancellation, admitted temporary storage
and cleanup for long transform chains.

[Ten independent rendering inputs](../text-paint-bounds/paint-stage-images.json)
cover two original R103/R104 radial pairs and three orthogonal group/use/symbol
scope pairs. Each uses one canonical conversion. All ten have different platform
pixels. Original opacity companions are unequal; the six scope controls keep
different source graphs despite equal native pixels. They protect the three
recording entry paths changed by this refactoring.

Three healthy, byte-stable native launches per host reproduce the four original
PNGs. The six scope fixtures also retain independent stable triples. Capture keeps
the exact original two-style font graph and pinned browser; source, font, native
context and image hashes remain checked. The existing regular and Mono licenses
apply. Existing references are reused; one original companion and the six scope
sources add their own files. No reference or tolerance is rewritten.

The full 960-input audit improves Windows exactness from 952 to 955; Linux remains
955. No formerly exact result is lost and no pixel error increases. Of the 39
retained precision witnesses, 31 now match both hosts and eight remain explicit
research. The remaining single-glyph alpha/color discrepancies and broader text,
shader and decoration reviews are not closed by this slice.

One [deep gradient contract](../../Lokad.Svg.Tests/SvgTextNodeMetricsTests.cs)
retains 80 draw stages and exercises admitted raster scratch, refusal, cancellation,
cleanup and retry. Existing matrix mechanisms, pattern recording/cache contracts,
text contracts and viewport/filter references remain in their original roles.

Affected qualification passes 14,545 core and 2,875 native/provenance checks on
Windows/Linux under both supported runtimes. Builds are warning-free. Single-run
validation costs, including host/discovery/TRX and excluding build:

| Host/runtime | Core | Native/provenance |
| --- | ---: | ---: |
| Windows .NET 11 | 44.63 s | 26.50 s |
| Linux .NET 11 | 43.59 s | 32.23 s |
| Windows .NET 10 | 46.36 s | 27.69 s |
| Linux .NET 10 | 44.32 s | 38.29 s |

These are local validation costs, not renderer performance measurements or a
before/after speed comparison. Full host/package qualification belongs to this
shared-stage milestone.
