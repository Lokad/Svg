# Resolved line stroke widths

The original CC0 [control](resolved-even-widths.svg) combines horizontal and
vertical axis lines, butt/square/round caps, dashes and a zero-width stroke.
Its physical lengths resolve to even widths that use optimized line lowering.
The [capture record](capture.json) freezes three agreeing healthy browser captures
per host before the correction. Identical platform PNGs share storage.

[Mechanisms](../../Lokad.Svg.Tests/SvgLineStrokeLengthTests.cs) check resolved widths,
declaration routes, odd/fractional outline dispatch and source-validation errors.
The [rendering regression](../../Lokad.Svg.VisualTests/SvgLineStrokeLengthReferenceTests.cs)
checks the independent reference through canonical conversion. It does not repeat
the API/ownership matrix. All inputs in this directory are original CC0 fixtures.

The [qualification](../release-baselines/unit-domain-contracts.json) records the
failing-before controls, full package-asset checks and unchanged original-source replay.
