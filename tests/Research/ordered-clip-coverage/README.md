# Ordered clip coverage

The [capture manifest](capture.json) retains 91 unique SVG/options records from
three healthy, stable native processes per input on Windows and Linux. The
[coverage index](coverage-index.json) preserves all 104 logical roles: the 78
original acceptance actuals, twelve path-order controls and fourteen interactions.
Only byte-identical source/options aliases share records. Different graphs retain
independent actual-input references, including their legitimate pixel differences.

The [published baseline](before-correction.json) has 68 exact inputs, 21 pixel
discrepancies and two affine painted-clip refusals. The bank covers overlapping,
adjacent, contained and disjoint children; affine placement; compound paths;
cyclic starts, directions and explicit closing points; and the 42/43-child
transition. Painted-clip controls include opacity, local use, object-box units,
output origin, multiple paints and authored rectangle/path identity.

Resolve original children once, preserving native winding and cyclic starts.
Collinear line emission must preserve closure identity. Raw authored controls
determine placed convexity for straight paths and curves. Large-child paint
replay retains its enclosing clip and authored child paint kind; apply source
coverage during painting, before mask multiplication, exactly once.

Nine direct [mechanism cases](../../Lokad.Svg.Tests/SvgOrderedClipTests.cs) test
these contracts; the [native boundary traces](native-boundaries.json) independently
record the two- and three-operand paths. Existing 42/43-child admission tests
retain their million-unit work limit. Five selected
[ownership/replay cases](../../Lokad.Svg.Tests/SvgOrderedClipOwnershipTests.cs)
cover borrowed ordered operands, affine fallback, isolated opacity, object-box
registration and prepared use replay, including refusal, cancellation, cleanup
and retry. The general 96-case API contract matrix remains separate.

Every rendering input uses canonical conversion against its own native image.
Equal PNG storage does not establish graph equivalence; platform references
remain independently captured even when their bytes match.
