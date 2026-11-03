# Font-relative text positions

These authored controls isolate inherited and overridden font sizes and a scalar
positioning list. Browser `em` positions match their numeric counterparts; the
numeric controls also match the library exactly. The relative input currently
rejects. [Capture evidence](capture.json) pins both inputs, programs, platform
references and three healthy captures of each input per host.

Resolve each declaration against its own computed font size before descendant
overrides consume an ancestor list. Root parsing, span parsing, list construction
and span cursors need the same length contract. Preserve existing numeric/px
arithmetic, original scalar ownership and ignored path Y positions. Rotation is
an angle, with a separate grammar.

The [ny1 review](../svgweb/ny1-static-review.json) reaches this gap after an explicit
RAM-only invalid-CSS repair. Neither that repair nor these supplied-font controls
qualify the unchanged hosted source.

To reproduce, supply the pinned Noto program to prepared conversion under
`DiagnosticNoto`. For native image capture, insert only an `@font-face` stylesheet
with that program embedded as a data URL, normal style, weight range 1–1000 and
`font-synthesis:none`; use a 192×144 image viewport at density 1. Confirm removing
the native stylesheet restores the parsed source. Follow the
[native capture procedure](../../../tools/README.md#capture-and-reference-rules).
Inputs use the repository license; the program retains its
[OFL notice](../supplied-font-labels/fonts/OFL.txt).
