# Text layout admission

Two authored controls demonstrate independent ordering requirements: Hebrew
bidirectional order and Devanagari pre-base vowel placement. The renderer admits
neither layout system. Supplying every glyph previously bypassed that boundary
and produced logical-order ink instead of an explicit `UnsupportedFeature`.

Each font aliases two scalars to the distinct A/V glyphs in the existing
[CC0 PlainKern program](../kerning-precedence/LICENSE.txt). Outlines, metrics,
instructions and Latin coverage are unchanged. The exact cmap recipe, source and
derivative hashes, browser pins and native binding are in [capture.json](capture.json).
Reproduction uses fontTools `TTFont(..., recalcTimestamp=False)`, adding the listed
aliases to every Unicode cmap before saving; no external font is needed.

For each source and its logical/visual ASCII controls, three fresh healthy native
captures on each host agree. Native sources match visual order; the pre-correction
library matches logical order, differing by 640 Windows or 660 Linux pixels.
Both ASCII controls already match native pixels exactly. The two sources protect
different mechanisms despite sharing resulting outlines. Frozen references and
pre-correction outputs are content-addressed and remain unchanged.

Direct mechanism tests check pinned Unicode direction, script, mark and surrogate
boundaries. Representative contracts check raw stream failure, prepared direct/
symbol/pattern demand, source diagnostics, no acquisition, cleanup and retry;
dormant text stays font-free. Four canonical rendering cases retain both ASCII
orders with the same programs, protecting admitted text when unused cmap entries
belong to unsupported scripts. Other API forms use the shared ownership suite.

The admission table conservatively excludes joining/syllabic scripts and Hangul,
plus RTL scalars and bidi controls; it does not implement a general shaper.
See the [current character contract](../../../docs/SUPPORT_MATRIX.md#text-and-fonts),
[SVG text layout](https://www.w3.org/TR/SVG2/text.html) and pinned
[Unicode 17 bidi rules](https://www.unicode.org/reports/tr9/tr9-51.html).
