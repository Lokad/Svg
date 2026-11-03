# Source addressing and emitted case conversion

The original CC0 controls use the shared [Noto Sans program](../supplied-font-labels/fonts/NotoSans-Regular.ttf)
under its [OFL](../supplied-font-labels/fonts/OFL.txt):

| Control | Independent rendering responsibility |
| --- | --- |
| [basic](basic/control.svg) | Uppercase expansion, inherited selection, a literal child override, ligatures and fitting. |
| [path](path/control.svg) | Expansion before a joined direct/path boundary, retaining child case selection. |
| [indexed](indexed/control.svg) | Original SVG x-list addresses after expansion; compare the independently authored [literal counterpart](indexed/reference.svg). |
| [lowercase](lowercase/control.svg) | Original-case context across spans and literal overrides, with a separate logical text end. |
| [lowercase-path](lowercase-path/control.svg) | Context across path boundaries and continuation whitespace, using an independent [literal counterpart](lowercase-path/reference.svg). |

Each capture recipe binds its source, font and native control hashes, with three
healthy profile-cleaned browser captures per platform. Basic/path capture their
own CSS inputs. Indexed captures the literal counterpart: the pinned browser's
transformed screenshot indexes this expansion differently from the [SVG source
rule](https://www.w3.org/TR/SVG2/text.html#TermSVGCharacter). The separate
[model comparison](../svgweb/text-casing-model.json) preserves both observed
browser behaviors; the library reference is not adjusted to that discrepancy.

The lowercase path also retains its [native CSS observations](lowercase-path/capture-css.json)
unchanged. Pinned Chrome restarts sigma context at path boundaries. The declared
source-context model instead uses the original text element throughout: this is
an inference from CSS ignoring inline boundaries during transformation and SVG
applying CSS layout before path mapping, with all text children treated as inline.
The literal counterpart has three healthy captures per host; the original CSS
observations remain distinct from that reference. [Context evidence](../svgweb/text-lowercase-context.json)
records both outcomes and the interpretation.

[Direct mechanisms](../../Lokad.Svg.Tests/SvgTextProjectionTests.cs) exercise the
[pinned Unicode mappings](unicode-data.json), inherited selection, original lists,
interval lifting, rotation, identity, boundaries and expansion storage. A temporary
storage refusal/cancellation/retry contract and one [prepared direct/instance
contract](../../Lokad.Svg.Tests/OpenTypeFontTests.Casing.cs) protect distinct owners.
The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgTextCaseReferenceTests.cs)
use one conversion path per control, without another API matrix.

[Lowercase mechanisms](../../Lokad.Svg.Tests/SvgLowercaseContextTests.cs) keep full
mapping, property boundaries and contextual rules direct. They distinguish logical
text endings from whitespace restarts, preserve the deferred character's font,
and use original characters when styles change. Deferred materialization refusal/
cancellation/retry remains representative ownership coverage.

The shared projection admits default Unicode uppercase/lowercase with unspecified/
English content language. Capitalization and other language tailoring remain open.
Existing font/shaping and character/glyph
bounds remain independent. [Qualification](../svgweb/text-lowercase-context.json)
records the exact scope and remaining original-source gates.
