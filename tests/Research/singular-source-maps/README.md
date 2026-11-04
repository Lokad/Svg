# Singular source maps

[The capture index](capture.json) preserves eight original zero-scale candidates
from the [alias review](../release-baselines/r85-alias-review.json) and 38 orthogonal
controls. Every admitted input uses canonical synchronous owned-PNG conversion.
Original caller font contexts and supplied-font bounds controls remain explicit;
native platform profiles retain their own references. All target captures use
three fresh browser processes per host. Sources and frozen reference pixels are
unchanged; baseline outcome fields retain the observation at the recorded source.

A singular child still contributes to an enclosing object's bounds and permits
its parent flood filter to paint. Effects inside the singular scope paint nothing.
Controls distinguish leaf/group/use/source and symbol placement, images,
non-scaling strokes, masks, clips, live aliases and required text metrics.

The [transform rendering model](https://www.w3.org/TR/css-transforms-1/#transform-function-lists)
suppresses objects and descendants under a noninvertible map. The implementation
uses the existing nonpainting source role and scene owners before lowering needs
an inverse, retaining raw/mapped bounds independently of paint. Text uses its
existing shaped layout bounds; shaping, fitting and ordinary rendering stay intact.

Mechanisms protect singular composition, source roles, direct/instance bounds,
aggregate command accounting and font liveness. Two representative contracts cover
prepared resource selection and memory refusal, cancellation, cleanup and retry.
No API matrix repeats across the image bank. The separately recorded root-transform
diagnostic remains outside the root attribute contract; paint-server inversion has
its own admission rules. [Qualification](../release-baselines/singular-source-qualification.json)
binds the complete original cohort, historical arc/rectangle admissions, runtime
gates and package consumers.
