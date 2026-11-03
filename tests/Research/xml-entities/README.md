# Internal XML entities

The shared BCL reader expands internal general entities before style, font,
resource and scene preparation. Production retains the original input and one
reader-workspace owner; it creates no expanded SVG copy or entity resolver.

[Mechanism tests](../../Lokad.Svg.Tests/SvgInternalEntityTests.cs) cover nested
markup, attribute normalization, declaration boundaries and the existing XML and
node ceilings. Ownership cases protect retained admission, cancellation, refusal,
cleanup and retry. One [prepared-font contract](../../Lokad.Svg.Tests/OpenTypeFontTests.XmlEntities.cs)
checks expanded instance discovery and a single program acquisition.

Three [canonical rendering cases](../../Lokad.Svg.VisualTests/SvgXmlEntityReferenceTests.cs)
use one conversion path each. Literal markup protects nested declarations;
literal and numerically encoded resources distinguish ordinary XML from names
invisible to raw-byte shortcuts. Inputs are independently authored, CC0-1.0.
[Capture metadata](capture.json) pins three healthy fresh Chrome processes per
case and host, using recipe 3. References and transparent pixels remain exact.

`nested-markup.svg` preserves a separate normalization discrepancy: the BCL
expands the numeric ampersand into a subsequent entity reference, while Chrome
renders black (66 different pixels on each host). Its input and native captures
remain research evidence. It is not an admitted exact rendering reference. The
[XML replacement rules](https://www.w3.org/TR/xml/#intern-replacement) and direct
mechanism assertions govern this normalization; no browser-specific rewrite is
applied to input or output.

The hosted `smile.svg` was replayed unmodified in RAM and matches three fresh
native captures per host. `preserveAspectRatio.svg` reaches its font requirement;
its unmodified XML with supplied Noto matches the independently captured expanded
graph with the same declared font binding. This retains the original Windows/Linux
font difference. Hosted SVG bodies are not stored here.

[Qualification](qualification.json) records source and binary identities, all-204
replay, validation times and the remaining milestone gates.
