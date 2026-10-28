# Point-list source maps and symbol viewports

The [capture manifest](capture.json) retains unchanged Chromium 153 PNGs from
three healthy, stable process captures per input and platform. Authored SVGs,
scaled capture inputs and native pixel hashes remain separate. Each actual
input is compared with its own browser reference through canonical synchronous
conversion; equivalent-looking graphs remain independent.

The [coverage index](coverage-index.json) accounts for 82 distinct native inputs:
44 corrected original gradient/viewport cases, 18 original exact controls and
20 additional acceptance inputs. Their authored bytes and options are distinct,
so all 82 remain individual rendering tests. The [baseline comparison](before-correction.json)
records 20 exact inputs before correction and 62 failing inputs; no reference
was changed to fit the implementation.

Coverage includes integer/decimal polygon and polyline spellings, positive and
negative large coordinates, direct/use/symbol gradient maps, output scaling,
reflection, nested viewports, opacity, alpha masks, patterns and user-space/radial
paints. Source geometry, paint-local bounds and viewport clips retain their own
coordinates through placement. Earlier scene-space rounding cannot trim the
device clip. Large-coordinate marker poses and own-filter regions remain open
correctness areas; this collection does not claim to close them.

Six [mechanism cases](../../Lokad.Svg.Tests/SvgPointListViewportTests.cs) exercise
source endpoint rounding, viewBox concatenation and the existing transform limit
directly. Existing numerical/parser banks retain exhaustive admission boundaries.
Two selected [ownership cases](../../Lokad.Svg.Tests/SvgPointListPaintOwnershipTests.cs)
cover source-paint and retained-viewport allocations, memory/work refusal,
cancellation, cleanup and retry. General destination and prepared-document
contracts remain in the representative API matrix.
