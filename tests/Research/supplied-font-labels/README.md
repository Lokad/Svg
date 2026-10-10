# Supplied public-font labels

The next whole document is the unchanged CC0 `logo-live-text.svg`, rendered at
64×64 with a transparent background. Seven CC0 controls cover curves/counters,
AV/space pairs, precomposed composites, fractional placement/size, small and large
sizes, and inherited alias/paint. They are separate from the document ledger.
**Pixel acceptance remains pending for this document and all seven controls.**

`font-pack.json` selects the unchanged, standalone static Noto Sans Regular 2.008
TrueType face 0 under the document's `Example` alias. The public program and OFL
notice are test assets; callers still supply font programs explicitly. No fonts
are bundled in the renderer, discovered from the host or read from `external/`.
`font-provenance.json` pins the upstream source and evaluation-data integrity.

Outputs/options were declared before capture or candidate screening. The pinned
Chrome 153.0.8010.52 image recipe received the original SVG with a browser-only
`@font-face` inserted in `defs`, embedding the exact font program as a base64 TTF
data URL under `Example`, weight 400 and normal style. Converter inputs retain
their original bytes and use a caller-owned catalog/provider/snapshot instead.

Each output has three fresh-process healthy, cleaned, stable captures on each
platform. Windows and Linux font pixels differ; `references/` and
`references/linux64/` freeze their respective native outputs. Their differences
are not tolerances and neither reference can be replaced to match the renderer.
No browser is launched during tests.

`font-use-proof/qualification.json` records missing, truncated and substituted
font witnesses captured **inside the SVG image**, with three healthy cleaned
captures per witness/platform. All differ from the intended-font image. The
substitution uses unchanged public Noto Serif Regular from the same upstream
revision and OFL license; its URL, byte size and program digest are recorded.
One PNG per stable triple is retained. This evidence complements the separate
browser DOM layout measurements; outer-page `document.fonts.ready` alone does
not qualify font use inside an image.

`metrics.json` contains independent FontTools cmap/hmtx/ordered kern evaluation
in font units mapped to user units. Browser metrics remain separately recorded:
native layout rounds advances and native glyph masks/hinting differ from
ordinary path pixels. Passing these layout/API checks does not count as an exact
document or control. The original single `A` must be corrected against its
frozen image; composites have an additional component-flag admission gate.

[The native raster diagnosis](diagnostics/native-rasterization.json) retains
intermediate data for that single `A`: a Windows hinted LCD coverage mask and a
Linux Fontations light-hint outline. The former matches its frozen image after
measured font preblend; existing managed ordinary-path rendering of the latter,
followed by Chrome's documented Linux font preblend, matches its frozen image.
These isolate the remaining managed hinting/mask work. They are diagnostic
inputs, never product substitutes, acceptance references or exact-font controls.
Neither document/control counts nor the original font/reference assets change.

[The Latin light contour witnesses](diagnostics/latin-light-contours.json)
retain eleven independently extracted native glyph outlines covering the current
`A`, `V`, `O` and `a` controls and their declared device sizes. The bounded managed
contour stage matches every witness, including small-stem rounding, the first
unaligned stem, curved counters and implied-point rounding. Synthetic controls
protect opposite winding, missing alignment samples, subpixel sizes, cancellation,
work expiry, memory denial and retry. The internal stage is not connected to
public glyph rasterization yet. These are geometry controls; the font bank stays
**0/1 completed documents and 0/7 exact pixel controls**. Connecting output-size
hinting, native contour coverage and font preblend is the next Linux step;
Windows grid fitting/LCD coverage and the remaining finite G9 subset stay open.
Complete qualification of the contour stage passes 6,331 tests on each platform
(4,442 core / 1,883 visual / six FontProbe). All 2,849 prior PNGs and 2,755
established exact results remain unchanged, including corporate 27/27 documents
and 258/258 variants. Frozen browser captures and evaluation assets are reused.

The bounded shared kerning reader admits ordered pair-list and class-pair
X-advance adjustments, coverage/class-definition formats 1/2, up to 32 selected
lookups and 32 subtables per lookup. The first matching subtable wins, including
zero adjustments; distinct lookups accumulate in LookupList order. IgnoreMarks
requires validated GDEF classes and rejects runs containing mark glyphs.
Chained-context format-2 coverage is a conservative unsupported-glyph guard,
including a sole glyph; its context rules are never executed. General shaping,
mark positioning and all remaining finite G9 items stay open in `PLAN.md`.
