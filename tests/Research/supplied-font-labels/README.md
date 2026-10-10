# Supplied public-font labels

The next whole document is the unchanged CC0 `logo-live-text.svg`, rendered at
64×64 with a transparent background. Seven CC0 controls cover curves/counters,
AV/space pairs, precomposed composites, fractional placement/size, small and large
sizes, and inherited alias/paint. They are separate from the document ledger.
**Linux is exact for the document and six admitted controls. Cross-platform
acceptance remains pending: Windows is not exact and composite accents are
still rejected.**

The next Windows dependency is an internal axial TrueType instruction stage.
[Independent native geometry](diagnostics/true-type-contours.json) supplies eleven
glyph/size outlines under two FreeType 2.13.2 interpreter query profiles, giving
**22/22 exact intermediate checks**. These are separate from Windows grid-fit
qualification and final font pixels. Public rendering does not call this stage;
the same original document, font and platform references remain selected.

The stage borrows `fpgm`, `prep` and simple-glyph instructions from the supplied
face. Its explicit target distinguishes CVT precision and preparation lifecycle;
the qualified probe restores initial CVT values and retains storage across the
default-to-grayscale transition. Bounded axial vectors, reference/zone state,
signed stack arithmetic, functions/branches, CVT/storage, point rounding/shifts,
deltas and untouched-point interpolation cover the executed programs. Limits are
4,096 stack/storage/CVT entries and twilight points, 1,024 functions, 32 call
levels and 65,535 bytes per program. Work and cancellation cover execution,
skipped operands, initialization and interpolation; arrays reserve before
allocation and release on failure. Synthetic controls protect these boundaries.
Executed nonaxial/unimplemented opcodes, IDEF declarations, composite instruction
replay and vertical phantom metrics remain explicit unsupported outcomes.
The Windows policy and symmetric LCD coverage still need their own qualification;
the wider finite G9 label/path subset remains open.

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
work expiry, memory denial and retry. These remain geometry controls, separately
from final pixels. Public Linux simple glyphs now use output-size hints,
quarter-X/pixel-Y origin rounding, shared native contour replay and font-mask
preblend before clip coverage and paint alpha. All six raw and six independently
prepared forms match unchanged Linux references: **1/1 document and 6/7 controls**.
Windows remains **0/1 and 0/7**, so cross-platform completion stays **0/1 and 0/7**.
Previously admitted composites retain their earlier rendering route; shared
composite hinting, the queued accent flags, Windows grid fitting/LCD coverage and
the remaining finite G9 subset stay open. Caller-supplied bytes remain borrowed.
Output-scale/viewBox controls reuse the frozen large-glyph reference and protect
mapping exactly once; tiny collapsed outlines, opacity rounding and owner cleanup
have separate synthetic controls.
Complete build/test qualification passes 6,444 tests on each platform
(4,504 core / 1,934 visual / six FontProbe). The earlier complete bank qualification
retains 2,849 PNGs and 2,755 established exact results, including corporate 27/27
documents and 258/258 variants. It is reused while the new internal stage has no
public rendering caller. Frozen browser captures and evaluation assets stay unchanged.

The bounded shared kerning reader admits ordered pair-list and class-pair
X-advance adjustments, coverage/class-definition formats 1/2, up to 32 selected
lookups and 32 subtables per lookup. The first matching subtable wins, including
zero adjustments; distinct lookups accumulate in LookupList order. IgnoreMarks
requires validated GDEF classes and rejects runs containing mark glyphs.
Chained-context format-2 coverage is a conservative unsupported-glyph guard,
including a sole glyph; its context rules are never executed. General shaping,
mark positioning and all remaining finite G9 items stay open in `PLAN.md`.
