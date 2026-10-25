# Offset and merge input rounding

The [capture record](capture.json) covers input rounding and affine filter image
restoration. Each SVG is compared with its own native image; merge and composite
graphs can differ.

Inputs cover curves, embedded RGBA images, source alpha, merge order, repeated
inputs, placement and operating color spaces. Stored sRGB image inputs use packed
source-over; color transfers and `SourceAlpha` retain the shader consumer.
General-path cases include fractional offsets and unused primitives with explicit
input connections, preserving the same result across merge execution paths.
Source aliases retain their consumer through offsets and authored-zero blur;
moved inputs keep their own native output.

Affine cases cover source rectangles, curves and ellipses, both working color
spaces, fractional output scales, rotations, reflections and shears around the
native sampling boundary. They retain image-edge coverage separately from
transparent-outset sampling. Smaller-shear controls preserve the padded route.
Composite and merge crops keep unstored pixels transparent when their native
image bounds extend beyond the compact output buffer.

Each input was captured in three healthy fresh browser launches per host. Windows
and Linux also agreed byte for byte. The 201 inputs share 131 reference files;
references are shared only when the captured bytes are identical.
