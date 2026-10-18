# Kerning precedence

The [document](kerning-precedence.svg) uses four original static TrueType fonts
to distinguish selected GPOS, zero GPOS adjustment, active legacy kerning and
unkerned text. GposKern and ZeroKern contain two valid legacy subtables; GPOS
takes precedence. The authored rows include AV, To and Wa at 32 and 16 pixels,
with integer and fractional origins. All SVG/font assets are [CC0](LICENSE.txt).

The [declaration](declaration.json) fixes the original rectangular contours,
table recipes and input identities. [HarfBuzz metrics](metrics.json) independently
record glyph IDs, positions, nominal advances and shaped advances. Legacy kerning
can distribute an adjustment across two glyph advances/offsets; the resulting
positions are compared rather than assuming the GPOS representation.

The [manifest](manifest.json) retains Chrome 153.0.8010.52 Windows/Linux
references from three healthy stable fresh processes, frozen before candidate
comparison. Image/inline captures agree, platform font reports identify every
intended program and changing each program independently changes decoded pixels.
Offline tests verify manifest acquisition/selection, independent metrics and exact
raw/prepared output. Focused controls preserve required legacy parsing, invalid
selected GPOS failures and SFNT bounds for unused tables.

This document qualifies table precedence. Lookup filtering and the original
Aptos/Calibri compatibility reports remain separate requirements; it does not
establish compatibility with unavailable proprietary font versions.
