# Offset and merge input rounding

The [capture record](capture.json) retains 100 rounding regressions, four
`SourceAlpha` controls, twelve single-input passthrough controls and a distinct
composite companion. Each SVG is compared with its own native image; merge and
composite graphs can differ.

Inputs cover curves, embedded RGBA images, source alpha, merge order, repeated
inputs, placement and operating color spaces. Stored sRGB image inputs use packed
source-over; color transfers and `SourceAlpha` retain the shader consumer.
General-path cases include fractional offsets and unused primitives with explicit
input connections, preserving the same result across merge execution paths.

Each input was captured in three healthy fresh browser launches per host. Windows
and Linux also agreed byte for byte. The 117 inputs share 81 reference files;
references are shared only when the captured bytes are identical.
