# Unpainted filter sources

The [capture](capture.json) keeps eight original regressions and twelve orthogonal
controls. Independent browser triples reproduce every target on Windows/Linux;
three PNG files share identical native images without merging source cases.

An ordinary unpainted shape keeps its own opacity inside its parent filter.
A synthetic empty-scope carrier keeps ancestor effects around generated output.
The controls retain solid/linear/radial/pattern paints, fill/stroke opacity,
display/visibility, parent/leaf filter placement and clipping.

All twenty images use one owned-PNG conversion. Original font snapshot contexts
remain explicit. Direct role/order mechanisms and one shared-budget contract
cover parsing, refusal, cancellation, cleanup and retry separately.

The [alias review](../release-baselines/r85-alias-review.json) preserves original
source/target identities, failing-before observations and native provenance.
