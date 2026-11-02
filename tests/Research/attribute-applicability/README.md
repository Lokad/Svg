# Attribute applicability

The original CC0 [control](attributes.svg) combines ignored attributes on roots,
groups, shapes, gradients and filters with visible geometry and paint. It retains
the distinction between literal `viewbox` and `viewBox`, and an invalid explicit
gradient-units value falling back to object-bounding-box paint.

[Direct mechanisms](../../Lokad.Svg.Tests/SvgAttributeApplicabilityTests.cs) cover
syntax locations, reader preservation, legacy feature guards and both gradient
parsers' unit/explicitness boundaries. One canonical conversion protects their
paint interaction using [three healthy captures per platform](capture.json).
Existing ownership regressions retain genuine unsupported inputs and cleanup
assertions; these rendering cases do not repeat the API matrix.

The [hosted-source review](../svgweb/attribute-applicability.json) records all 204
default outcomes and the independent native comparisons. Hosted SVG bodies and
variants remain in memory; this bank contains only the independently authored
control and its references.
