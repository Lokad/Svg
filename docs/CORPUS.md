# Fixture corpus and provenance

`tests/Fixtures/manifest.json` is the authoritative fixed-inventory catalog.
Every asset needs a stable ID, byte hashes, provenance, rights evidence, dimensions,
feature tags, expected semantic support and an independent fidelity status.

The initial probes are original CC0 SVGs. A fidelity status is never changed merely
because a candidate renderer produces an image: it requires a reviewed reference and
an exact independent comparison.

`original-solid-red` has an admitted stable pinned-Chrome reference and one exact
candidate comparison. Its manifest records the candidate hash and zero-difference
result. A reference artifact alone is not a renderer pass; this explicit comparison
is the evidence for its `exact` status.

`original-line-rectangle-path` exercises the supported absolute line-segment
spelling of a closed axis-aligned integer rectangle. `original-orthogonal-polygon`
`original-orthogonal-path`, and `original-orthogonal-polyline` exercise the supported
L-shaped band decomposition.
`original-fractional-overlap` has a stable pinned-Chrome reference but is
explicitly `unsupported`: it uses fractional geometry, which the initial rectangle
subset does not implement. This is retained in the inventory rather than hidden
from fidelity reporting.

`original-integer-alpha-overlap` has an admitted stable pinned-Chrome reference and
an exact candidate comparison. It pins the integer source-over rounding behavior for
two half-opaque layers on a transparent surface.

`original-inline-styles` has an admitted stable pinned-Chrome reference and exact
candidate comparison. It pins inline-style precedence, inherited `currentColor`,
and alpha paint within the currently supported rectangle subset.

`original-viewbox-meet` has an admitted stable pinned-Chrome reference and exact
candidate comparison. It pins default `xMidYMid meet` centering, a nonzero viewBox
origin, and integral viewBox mapping.

`original-viewbox-none` has an admitted stable pinned-Chrome reference and exact
candidate comparison. It pins the supported nonuniform `preserveAspectRatio="none"`
mapping.

When a Chrome reference is admitted, record the pinned browser/archive hash, platform,
recipe revision, input/materialized hashes, resource hashes and capture options in the
manifest and `tests/References/`. References must be generated from the original bytes;
paths and text must never be rewritten to favour the candidate renderer.
