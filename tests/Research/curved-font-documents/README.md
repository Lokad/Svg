# Retained curved label document

The next whole G9/G3 deliverable is Dr. Olaf Hoffmann's public-domain
[SVGtext-anchor04.svg](https://commons.wikimedia.org/wiki/File:SVGtext-anchor04.svg).
The retained [original](../../Fixtures/candidates/inv-127.svg) preserves all
3,351 upstream bytes, including its ISO-8859-1 declaration, inert public SVG
DOCTYPE, German labels, eight cubic paths, calibrated lengths and dashed guides.
The Commons author's PD-self dedication and retrieval metadata are retained in
[source-records.json](source-records.json).

Before native capture or renderer screening, selection at `58c63d70` declared
one explicit **512×369 transparent viewport** and the unchanged public static
Noto Sans Regular face 0 under the document's original `fantasy` alias. This is
an explicit caller catalog mapping. The program and OFL notice are borrowed from
[the existing font bank](../supplied-font-labels/fonts); no second font copy or
host font discovery is involved. [The manifest](manifest.json) owns the source,
output and matching-platform reference contract.

Document completion is **0/1**. Three of five separate controls are exact on
both platforms through all six raw and six independently prepared outputs.
Linux also completes the composite control, giving four exact controls there.
Shared open-curve distance/tangent placement and `USE_MY_METRICS` component inheritance
now let the unchanged document render. Matching-platform glyph-mask fidelity
remains open, including the precomposed accents. Finish the whole
document and all discovered mismatches against its frozen references. Controls
do not count as completed documents.

| Control | Shared dependency | Current renderer outcome |
| --- | --- | --- |
| Two path runs | Independent sibling layout/paint slots | Exact on both platforms through 12 APIs. |
| Per-path anchor | Child anchor overriding parent | Exact on both platforms through 12 APIs. |
| Calibrated path length | Numeric offsets scaled separately from glyph advances | Exact on both platforms through 12 APIs. |
| Curved midpoint | Curved length, tangent and glyph mapping | Renders through 12 APIs; 14 Windows / 36 Linux pixels still differ. |
| Composite on path | Supplied precomposed `ü` placement and fitting | Exact on Linux through 12 APIs; Windows still differs at 8 pixels. |

[Independent native placement](diagnostics/curved-midpoint-native-placement.json)
records three identical healthy intended-font browser measurements and screenshots
matching the frozen curved control. Its origin agrees within one millionth of a
pixel. DOM `getPointAtLength` uses a different distance resolution from text layout;
its separate samples must not replace the text-layout contour measure. Focused
checks protect zero-angle polynomial strikes, endpoint tangent fallback, bounded
work/cancellation, retained map ownership and denial cleanup. They do not increase
the document or frozen-control completion counts.

[Linux native placement](diagnostics/curved-midpoint-linux-native-placement.json)
retains three further healthy intended-font measurements against the unchanged
Linux reference. Mapped glyphs now choose native quarter-pixel phases on both axes
after viewport placement, while preserving the tangent transform. This reduces
Linux curved-control differences from 80 to 36 and whole-document differences
from 20,917 to 11,962. Native scaler/transform and glyph-mask coverage remain open;
standalone contour agreement does not complete the document.

The five synthetic controls are original fixtures dedicated to
[CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/). Their dimensions,
font contract and references were frozen before renderer screening too.

Chrome 153.0.8010.52 produced three identical healthy captures per source and
platform, with cleaned profiles. Windows and Linux references intentionally
retain their own native font pixels. The image-data capture path rejects
non-ASCII Latin-1 input: an ASCII Latin-1 image decodes, while non-ASCII text does
not, including without a DOCTYPE. Browser-only preparation therefore transcodes
the declared original text to UTF-8 and changes its encoding declaration, inserts
the declared root width/height and embeds the supplied face as
`LokadSuppliedFantasy`. The selector `svg,text[font-family="fantasy"]` maps the
root and the authored explicit text family to that face, normal weight 400.
Removing only this injected style and root dimensions yields the same parsed
XML tree as the original. Product inputs retain their original Latin-1 bytes.
[The encoding receipt](encoding-qualification.json) records this data equivalence.

[Font-use proof](font-use-proof/qualification.json) retains healthy stable triples
and one image per missing, corrupt and substituted embedding on each platform.
Missing/corrupt embeddings change 41,132 Windows and 37,449 Linux pixels;
substituting public Noto Serif changes 40,802 and 37,621 respectively. The font
is therefore active inside the captured SVG image. These are capture controls,
never renderer inputs or document completion results.

Regression tests protect frozen evaluation data, the three completed controls
through twelve APIs, remaining categorized failures, container/child anchor
inheritance, calibration, aggregate character limits and owner cleanup. Keep
implementation source, build, DLL and script fingerprints out of this bank.

[Independent composite contours](diagnostics/composite-metrics-contours.json)
protect the original `é`, `Å` and `ü` at the declared control sizes under both
native interpreter profiles. Four separate diagnostic font copies make a parent
hint depend on the selected child's metric point, including transformed and nested
components. They preserve the original supplied face and reference pixels. Bounded
denial, cancellation and retry checks protect the new assembly dependency.
