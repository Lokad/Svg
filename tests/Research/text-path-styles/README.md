# Text-path child styles

Text paths and spans share font, paint, whitespace and anchor resolution. Path
placement retains the computed CSS anchor, including when the effective font
interval equals its parent. Reference syntax and element-specific positioning
remain separate from presentation; no glyph-layout or rounding algorithm changes.

These original CC0 fixtures each have a styled `control.svg` and an independently
authored `reference.svg` using the previously admitted subset:

| Fixture | Interaction protected |
| --- | --- |
| [Basic](basic/control.svg) | XML font size/anchor, inline paint and class rules with important precedence. |
| [Cascade](cascade/control.svg) | CSS overrides an XML anchor to equal the parent, exercising font-interval elision. |
| [Nested](nested/control.svg) | Path styles and nested span font/paint/anchor selection. |
| [Mixed](mixed/control.svg) | Direct text, styled path text and a following direct range restore their own styles. |

Each folder's `capture.json` records source/font hashes and three healthy fresh
Chrome processes per variant and platform: 48 captures total. All repeated
captures are stable; each styled/literal pair has zero differing pixels per host.
Separate Windows/Linux PNGs preserve platform fitting differences. Canonical tests
compare the styled input directly with its native capture. Literal captures remain
diagnostic evidence: the preceding library renders them exactly and rejects all
four styled forms. Equal pixels do not make their source roles interchangeable.

All fixtures borrow the unchanged [Noto Sans font and license](../supplied-font-labels/README.md).
Native capture embeds that program in memory; manifests select it explicitly for
the library, without machine-font lookup or copied font bytes.

[Mechanisms](../../Lokad.Svg.Tests/SvgTextPathStyleTests.cs) cover cascade values,
inapplicable coordinates, retained unsupported features, both reference forms'
identifier boundaries and direct/instance emitted-font needs. Two
[contracts](../../Lokad.Svg.Tests/OpenTypeFontTests.TextPathStyles.cs) protect
selective font acquisition and borrowed-font/storage cleanup, refusal, cancellation
and retry. Four [rendering cases](../../Lokad.Svg.VisualTests/SvgTextPathStyleReferenceTests.cs)
use the existing canonical font-fixture helper. No new test framework or fixture/API
product is added. [Qualification](../svgweb/text-path-styles.json) records exact
before/after assemblies, timings and remaining original-source limitations.
