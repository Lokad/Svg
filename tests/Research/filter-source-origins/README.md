# Filter source origins

These independently authored CC0 controls distinguish compositor bitmap placement
from compact source storage. A filter already rendering in a compositor bitmap
must enter its compact source bitmap before drawing and sampling. Keeping only
the compositor origin can move the source or erase it completely.

The four [canonical rendering cases](../../Lokad.Svg.VisualTests/SvgFilterSourceOriginReferenceTests.cs)
cover a compact gradient control, a second-tile solid, a second-tile gradient and
a source crossing a tile boundary. Solid and gradient paints retain distinct
construction paths. The two-shadow graph retains intermediate images and offsets;
it is not reducible to an unfiltered translation control.

[Capture recipes](capture.json) identify unchanged source bytes and independent
Windows/Linux observations, with three healthy, profile-cleaned processes per
case/host. Pixel comparisons are exact. One
[ownership contract](../../Lokad.Svg.Tests/SvgFilterSourceOriginOwnershipTests.cs)
uses the existing refusal, cancellation, cleanup and retry helper. Core output
contains only that source and its [hash slice](ownership.json).

[Qualification](../svgweb/filter-source-origins.json) records the failing-before
evidence and the separate hosted static/font diagnostic. Hosted SVG bodies and
attribute values are not retained. The static diagnostic does not qualify
animation or the original font environment.

The separate [gradient sampling controls](../filter-gradient-sampling/README.md)
retain the two further findings and their unchanged native observations.
