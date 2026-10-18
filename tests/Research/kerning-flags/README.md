# Pair-positioning lookup filters

The [thirteen-row SVG](kerning-flags.svg) uses seven original static TrueType
fonts with CC0 rectangular outlines. It covers RIGHT_TO_LEFT on pair lookups,
base/ligature filtering, and admission of mark-related GDEF metadata for supported
non-mark labels. Integer/fractional origins, two sizes, and font/size/absolute
position boundaries share the complete document. All input/font assets are
[CC0](LICENSE.txt); no private font pack is needed.

The [declaration](declaration.json) fixes each lookup's flags, classes, pairs and
selected mark set. [HarfBuzz 8.3 metrics](metrics.json) independently record
eighteen declared font/size/position runs, including glyph IDs, source clusters,
nominal advances, adjusted advances and positions. Independent sequences show
that ignored starting glyphs do not kern; eligible glyphs can pair across an
ignored base or ligature. Each lookup selects its own partner.

The [manifest](manifest.json) preserves Chrome 153.0.8010.52 Windows/Linux
references frozen before candidate comparison: three healthy stable fresh-process
captures, image/inline agreement, intended custom-font reports, and a separate
changed-program control for each of the seven fonts. Offline tests verify asset
identities, acquisition/selection, independent metrics and exact raw/prepared
pixels. Focused controls additionally protect lookahead across later font/size/
absolute boundaries, ligature and supplementary-scalar source lengths, lookup
ordering/zero adjustment, malformed definitions, work/cancellation and cleanup.

RIGHT_TO_LEFT is ignored for type-2 lookups; it does not enable bidi text.
IgnoreBaseGlyphs/IgnoreLigatures filter GDEF classes 1/2. IgnoreMarks, attachment
classes and mark-filtering sets require valid referenced definitions, while
actual GDEF-mark runs keep an explicit unsupported outcome. Metadata admission
does not implement mark positioning, filtered contextual lookups or general
shaping. The original Aptos/Calibri compatibility reports remain separately
pending; these public fonts qualify shared mechanisms only.
