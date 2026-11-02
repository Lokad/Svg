# Conditional shift targets

These original CC0 programs distinguish eligible vertical pixel deltas from
untouched, horizontal, oblique and completed-interpolation targets. Composite
targets retain their separate allowance. Recognition of the exact conditional
function keeps these rules even with the native-mode waiver; inline and altered
bodies keep ordinary `SHPIX` behavior.

The [native coordinates](native-targets.json) cover fourteen cases in each of the
natural and symmetric Windows profiles. Integer bands transport point values
after the tested operations. The [mechanism tests](../../Lokad.Svg.Tests/OpenTypeFontTests.ConditionalTargets.cs)
exercise both simple and composite loaders directly, including signs, loop target
selection, touch state and interpolation. Existing stack and loop assertions remain
in the [companion tests](../../Lokad.Svg.Tests/TrueTypeHintingTests.ConditionalShift.cs).

The [SVG](conditional-targets.svg) paints four controls through one canonical
conversion. [Capture evidence](capture.json) retains three fresh healthy processes
per host, independent platform references and positive font-use checks. These
controls use no private glyph or hosted SVG geometry.

The [derivation](derive.py) reproduces both font hashes with development-only
fontTools 4.66.1; follow the [tool procedure](../../../tools/README.md#font-program-controls).
