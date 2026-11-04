# Text clipping

The nineteen [canonical cases](capture.json) compare exact RGBA through one
conversion path. Each reference comes from three fresh healthy, cleaned pinned
browser processes on its own platform. The first twelve inputs and PNGs are
borrowed unchanged from the [mask recording experiments](../mask-text-recording/clip-research.json).
The supplied Noto program and its license remain in
[supplied-font-labels](../supplied-font-labels/fonts/OFL.txt).

The cases separate child/consumer transforms, object-box units, fractional
origins, source opacity, span paint, positioning, textLength, mixed geometry,
intersections, inherited typography, decorations, referenced text and geometry,
and group isolation. Equal pixels in the inherited-font and identity cases do
not make their font-selection paths interchangeable.

Text clips reuse retained text, shaping and layout. Paint purpose supplies black
fill and no stroke/filter before paint resource selection. Geometric path clips
ignore paint opacity; ordered mask replay preserves geometric source opacity
and use-instance opacity separately. Invisible text can still supply a painted
picture's enclosing layout bounds. A group's own opacity restores after its clip.
These distinctions protect rounding and antialiasing boundaries without replacing
clip coverage with ordinary alpha-mask restoration.

[Font-selection contracts](../../Lokad.Svg.Tests/OpenTypeFontTests.TextClips.cs)
separate live, unused, hidden, zero-opacity, empty and dormant-group roles, ordinary
ID reuse, ignored paint, cycles and layout-only dependencies. One representative
prepared-document case checks memory refusal, acquisition cancellation, cleanup,
retry and output ownership. These contracts do not repeat across the images.

The [declared original diagnostic](original-static.json) records the independent
`masking-path-04-b.svg` captures with authored PNG resources and supplied Noto.
Its hashed projection recipe remains in the
[original gate ledger](../svgweb/remaining-static-gates.json). Original external
resources/fonts and dynamic behavior remain separate qualification areas. No
hosted SVG bodies, fragments or raw attribute values are retained.

[Qualification](qualification.json) records before failures, exact comparisons
and validation times. Authored SVGs are CC0-1.0. Routine tests run offline without
Python or a browser; follow the [capture rules](../../../tools/README.md#capture-and-reference-rules)
when reproducing independent evidence.
