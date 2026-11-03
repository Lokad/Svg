# Filter destination composition

These original CC0 controls exercise the shared per-bitmap filter path. Final
colour transfer, floating paint, opacity and clipping must meet the owning
destination. An extra fractional-offset replay into a byte bitmap consumed those
stages before composition; it also repeated source and clipping setup.

The [five canonical cases](../../Lokad.Svg.VisualTests/SvgFilterBitmapRestoreReferenceTests.cs)
separate wide linearRGB composition, its sRGB control, compact composition,
nonopaque restoration and clipping across a tile boundary. Their
[native recipe](capture.json) records three healthy, profile-cleaned processes
per case on Windows and Linux: 30 independent captures. References are exact.

One [direct mechanism](../../Lokad.Svg.Tests/SvgFilterBitmapRestoreTests.cs)
pins low-alpha colour transfer before destination blending. One ownership case
uses the existing refusal, cancellation, cleanup and retry helper. Core output
contains only that input and its [hash slice](ownership.json). Three ownership
classes share ordinary bank-loading setup; their distinct cases and assertions
remain individually selectable.

[Qualification](../svgweb/filter-destination-composition.json) records before/after
evidence and the declared hosted static/font context. That complete diagnostic is
exact; [terminal colour coverage](../svgweb/filter-color-program-coverage.json)
closes the separate prefix crop fringe. No hosted SVG bodies or attribute values
are retained. Animation and the original font environment are outside this
diagnostic's acceptance claim.
