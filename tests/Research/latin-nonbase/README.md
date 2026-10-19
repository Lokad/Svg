# Latin non-base fitting

These original CC0 static TrueType fixtures isolate glyph classification from
outline geometry. A and degree contain the same clockwise box, while H, x and o
supply ordinary Latin alignment/stem samples. Copyright uses a composite copy.
Base.ttf maps degree separately; Alias.ttf maps A and degree to the same glyph.
Both files use UPEM 1000 and 600-unit advances. No glyph has instructions.

[Independent vectors](native-vectors.json) were produced with Skrifa 0.43.2,
read-fonts 0.40.1 and font-types 0.12.0, Auto/Light, at 11.5 and 16 px. They cover
every mapped test glyph, including the cmap alias and composite. Non-base glyphs
retain global scale adjustment and stem fitting but skip alignment-zone matches.
The alias affects the glyph even when requested through A.

The offline tests assert all four contour edges against the native vectors,
unchanged source bytes, work limits, cancellation and released storage. These
controls qualify fitting behavior; they introduce no mark shaping or new text
scope. Private face documents remain separate complete-document acceptance cases.

The unmapped degree glyph in Alias.ttf is unused diagnostic storage, not an
authored SVG case or a public glyph-index API. The declared vectors cover only
mapped glyphs. All fixture assets are [CC0](LICENSE.txt).
