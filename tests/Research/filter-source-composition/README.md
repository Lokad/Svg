# Historical source/composition qualification

These 2,253 inputs preserve the original Chromium Canvas `drawImage` references
for source capture and filter composition. All three original captures have
identical decoded pixels on each platform; the [manifest](capture.json) retains
their hashes and capture profile. The [baseline comparison](before-correction.json)
records 877 inexact inputs per platform before correction. Native images are
copied unchanged, including naturally unequal graph results.

The [coverage index](coverage-index.json) accounts for all 2,432 original roles
and 46 focused roles. A role shares an existing test only when its authored SVG
bytes, font, output scale, background, viewport options and native pixels on
both platforms agree. Equal images alone do not establish redundancy. The index
maps 145 historical roles to existing [offset/merge inputs](../offset-merge-inputs/),
20 to the new [focused controls](../filter-merge-image-draw/README.md), and the
remaining 2,267 roles to these 2,253 individual tests.

[Rendering tests](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) run
every input through one canonical conversion. The [research selection](research-cases.json)
keeps this exploratory matrix in full CI and periodic qualification; use
`--filter 'Category!=Research'` for the shorter development loop. It does not
multiply these inputs across twelve API forms. The focused controls retain
failing-before interactions, platform-specific fonts and representative ownership
checks in routine validation.
