# Filter source images and merge drawing

These 45 actual inputs have independent Windows/Linux references from three
healthy fresh-process page captures each. The [manifest](capture.json) binds SVG,
font and reference hashes; repeated PNGs are byte-identical. Scaled font cases
retain the separately sized native capture input. No reference was rebaselined.
See the [capture procedure](../../../tools/README.md#research-probes).

The [baseline comparison](before-correction.json) records 27 failing inputs on
Windows and 25 on Linux before correction, alongside positive controls. Cases
cover transformed source capture, fractional and negative offsets, source alpha,
mixed operating spaces, cropped reuse, prior blur, embedded images, supplied
fonts and active use/symbol/pattern/mask consumers. Merge and composite inputs
keep their own references, including naturally unequal native images.

[Rendering tests](../../Lokad.Svg.VisualTests/SvgPaintInputReferenceTests.cs) use
one canonical conversion per input. [Bounds mechanisms](../../Lokad.Svg.Tests/SvgFilterSourceHintTests.cs)
check rounding and coordinate admission directly. Two representative
[ownership cases](../../Lokad.Svg.Tests/SvgFilterBitmapOwnershipTests.cs) protect
affine clipped image drawing and mixed-space allocation, cancellation, denial,
cleanup, retry and prepared replay without repeating the API matrix per fixture.

The [historical corpus](../filter-source-composition/README.md) retains broader
periodic qualification and maps every original role to its individual test.
