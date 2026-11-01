# Conditional pixel-shift functions

The original CC0 [font recipe](font-recipe.json) and [SVG](conditional-functions.svg)
isolate Windows recognition of one exact conditional `SHPIX` function body.
Recognition follows the function body across renaming. Its branch, stack and loop
effects remain, while movement is suppressed. Equivalent inline code and an inert
prefix or suffix execute their shifts. Native mode and completed interpolation
do not change that distinction. The outside-size-range control retains the
ordinary false branch.

[Capture provenance](capture.json) pins the input, program, browser and separate
Windows/Linux references: three agreeing fresh healthy processes per host.
Missing-font and changed-geometry probes prove use of the supplied program.
Linux light fitting preserves its independent result.

Eight direct [mechanism cases](../../Lokad.Svg.Tests/TrueTypeHintingTests.ConditionalShift.cs)
protect function identity, ordinary execution, stack consumption, loop reset,
touch flags and cleanup. One canonical [rendering test](../../Lokad.Svg.VisualTests/SvgWindowsScanBoundaryTests.cs)
covers the native font behavior. No private glyph or hosted SVG geometry is copied.
