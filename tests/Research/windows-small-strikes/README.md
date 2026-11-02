# Windows small raster strikes

These original CC0 rectangle fonts distinguish program admission, component
fitting and scanner selection. No hosted geometry or private font bytes are used.
The [native masks](native-masks.json) retain 48 distinct DirectWrite cases, each
captured three times, plus two unfitted-loader companions.

The maximum raster axis admits programs at rounded 2ppem. Size first passes
through native 16.16 precision; the integer-size head flag acts before that step.
A component below the cutoff still executes when its parent raster admitted
fitting. Scanner thresholds instead use the vertical raster size: both LCD
profiles default to simple dropouts including stubs through rounded 32ppem.
Authored scanner settings remain distinct. An instruction-free parent selects
its prepared scanner state while retaining its selected child's metrics.

[Direct tests](../../Lokad.Svg.Tests/OpenTypeFontTests.SmallStrikes.cs) cover the
cutoffs, fractional axes, integer sizing, simple/composite programs, scaled
children, authored thresholds and unfitted admission. They compare every raw
RGB alpha byte and assert released contour storage. The existing interpreter,
allocation and cancellation contracts remain separate.

The [canonical SVG](small-strikes.svg) renders the same marker at 1px and 2px.
[Capture evidence](capture.json) retains three healthy, profile-cleaned browser
processes per platform and independent Windows/Linux references. The larger
companion protects execution above the cutoff; Linux keeps its own fitting.
The [derivation](derive.py) reproduces all five font hashes with development-only
fontTools 4.66.1, following the [tool procedure](../../../tools/README.md#font-program-controls).
