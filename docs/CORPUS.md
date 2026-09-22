# Fixture corpus and provenance

`tests/Fixtures/manifest.json` is the authoritative fixed-inventory catalog.
Every asset needs a stable ID, byte hashes, provenance, rights evidence, dimensions,
feature tags, expected semantic support and an independent fidelity status.

The initial probes are original CC0 SVGs. A fidelity status is never changed merely
because a candidate renderer produces an image: it requires a reviewed reference and
an exact independent comparison. The visual test suite reads the manifest and renders
and compares every current `exact` entry; a reference artifact alone is not a
renderer pass. Entries inventoried as `unsupported` carry provenance, hashes
and an explicit reason and are excluded from pixel comparison until admitted.

Each materialized asset begins with its CC0 SPDX declaration. `originalSha256`
preserves the pre-materialization bytes where that declaration was added;
`materializedSha256` identifies the bytes actually rendered and compared.

`original-line-rectangle-path` exercises the supported absolute line-segment
spelling of a closed axis-aligned integer rectangle. `original-orthogonal-polygon`
`original-orthogonal-path`, and `original-orthogonal-polyline` exercise the supported
L-shaped band decomposition.
`original-fractional-overlap` validates the supported analytic fractional
rectangle coverage and calibrated source-over rounding against a transparent
pinned-Chrome reference.

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

`original-logo-link-use-mark` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-nested-svg` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-symbol-viewport` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-clipped-mark` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-mask-mark` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-embedded-image` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-pattern-mark` has an admitted stable pinned-Chrome reference and exact candidate comparison.

`original-logo-gradient-mark` has an admitted stable pinned-Chrome reference and exact candidate comparison. It pins a diagonal two-stop objectBoundingBox gradient shared across two aspect ratios (position evaluated in normalized bounding-box space); multi-stop, radial, transformed and non-pad gradients stay explicitly unsupported. It pins integer-grid `userSpaceOnUse` pattern tiling of a rectangle (8-pixel red tile on a 16-pixel grid); objectBoundingBox units, fractional grids, overflowing or multi-rectangle tiles, nested patterns, definition reuse and non-rectangle pattern paints stay explicitly unsupported. It pins a solid-color 1-pixel PNG data URI stretched to a 48-pixel square (identical pixels to the mask mark, shared reference bytes); non-solid, interlaced, palette, color-managed and corrupt images stay explicitly rejected with direct decoder regressions. It pins a white single-rectangle `mask` modulating an opaque red leaf; a gray-mask scratch comparison also matched Chrome exactly (grayscale linearity), while chromatic luminance content, masked groups/text and custom regions stay explicitly unsupported. It pins single-rectangle `clipPath` clipping of a group (red/blue bars through a 32-pixel window); multi-rectangle clips, forward clip references, clipped text and masks stay explicitly unsupported. It pins symbol `viewBox` mapping through a dimension-omitted `use` (100%-of-viewport default, 4x scale); parser tests pin explicit dimensions, viewBox-less 1:1 mapping, meet-centering, group-transform composition and instance fill override, each verified against discriminating Chrome captures during development. It pins nested-viewport origin offset with exact box clipping (32-pixel red square at (8, 8)); viewport `viewBox`/percentage dimensions and viewport text stay explicitly unsupported. It pins local `use` instantiation nested directly in a link (two bars) alongside a direct rectangle; group-nested instances and enclosing-transform composition are pinned by the converter matrix and unit tests.

`original-logo-outlined-counter` has an admitted stable pinned-Chrome reference and exact candidate comparison. It pins disjoint orthogonal multi-subpath paths with `evenodd`/`nonzero` fill rules (counter hole versus solid); intersecting, unclosed and non-orthogonal subpaths stay explicitly unsupported. A minified relative-compact spelling variant renders byte-identical captures against the same reference bytes.

When a Chrome reference is admitted, record the pinned browser/archive hash, platform,
recipe revision, input/materialized hashes, resource hashes and capture options in the
manifest and `tests/References/`. References must be generated from the original bytes;
paths and text must never be rewritten to favour the candidate renderer.
