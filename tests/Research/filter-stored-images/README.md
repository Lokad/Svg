# Stored filter images

Nineteen inputs retain their own Windows and Linux browser images. All fail before
the stored-image corrections. [Capture metadata](capture.json) records hashes and
three identical, healthy fresh-process captures per input. See the
[capture procedures](../../../tools/README.md).

A terminal nearest offset retains its input image after blur or graph
materialization. Offset results carry their transformed stored extent; multi-input
merges carry the union of their input images. Primitive regions constrain these
extents without supplying pixels. Source-alpha materialization keeps floating
coverage rounding before the next image operation.

Controls cover canceling and consecutive offsets, operating color spaces, aliases,
ancestor clips, reflection, backdrops, strokes, use and pattern replay. Curved and
rectangular low-alpha images distinguish shader storage from packed bitmap and
constant rounding. Existing [source-alpha controls](../filter-alpha-stages/README.md)
and [image placement controls](../filter-image-placement/README.md) preserve their
independent positive paths and naturally unequal references.

The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs)
compare every input with its own image. Seventeen direct mechanism cases cover
stored extents and alpha/coverage boundaries. One selected merged input checks
scratch refusal, cleanup, cancellation, retry and prepared replay. These cases
retain separate responsibilities without repeating the API matrix.
