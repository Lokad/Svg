# Font-relative text positions

These authored controls isolate inherited and overridden font sizes and a scalar
positioning list. Browser `em` positions match their numeric counterparts; the
numeric and relative controls match the library exactly. [Capture evidence](capture.json)
pins the before failure, both inputs, programs, platform
references and three healthy captures of each input per host.

Resolve each declaration against its own computed font size before descendant
overrides consume an ancestor list. Root parsing, span parsing, list construction
and span cursors need the same length contract. Preserve existing numeric/px
arithmetic, original scalar ownership and ignored path Y positions. Rotation is
an angle, with a separate grammar.

The [rounding observations](rounding-dom.json) distinguish native double-precision
unit resolution from early float multiplication. Load each authored input in that
record's pinned font context, wait for `document.fonts.ready`, then query the text's
`getBBox()`, `getStartPositionOfChar(0)`, `getEndPositionOfChar(0)` and
`getRotationOfChar(0)`. Each host has three healthy identical observations.

The [ny1 review](../svgweb/ny1-static-review.json) now renders after an explicit
RAM-only invalid-CSS repair. Its twelve residual separator pixels remain unchanged
after removing every text element. Neither that repair nor supplied fonts qualify
the unchanged hosted source.

To reproduce, supply the pinned Noto program to prepared conversion under
`DiagnosticNoto`. For native image capture, insert only an `@font-face` stylesheet
with that program embedded as a data URL, normal style, weight range 1–1000 and
`font-synthesis:none`; use a 192×144 image viewport at density 1. Confirm removing
the native stylesheet restores the parsed source. Follow the
[native capture procedure](../../../tools/README.md#capture-and-reference-rules).
Inputs use the repository license; the program retains its
[OFL notice](../supplied-font-labels/fonts/OFL.txt).
