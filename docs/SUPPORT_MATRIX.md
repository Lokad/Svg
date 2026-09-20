# Support matrix

## Current state

The renderer is a narrow prototype. It supports only the explicit subset listed
below, and the accepted-input contract gaps recorded in [PLAN.md](../PLAN.md) remain
open. Do not infer that every unlisted visible SVG construct is rejected correctly.
All forty-three current manifest entries have exact decoded-pixel render gates; this
is not a general pixel-fidelity claim.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented; current rectangle raster work is preflight-bounded. Parsing reserves the greater of 16 KiB, four times input bytes, or 512 bytes per configured XML-attribute limit before `XmlReader` construction; it is an accounted renderer allowance, not a process-RSS cap or exact BCL allocation measure |
| Root SVG parsing | Bounded validation counts every descendant element and rejects XML nesting beyond the configured `MaximumXmlDepth` (128 by default) or more than `MaximumAttributesPerElement` attributes (256 by default) before this parser consumes them; inert `id`, title/description/metadata and unused definitions; self-closing rectangle or supported axis-aligned-line definitions in attribute-free direct `defs`, or local `symbol` definitions containing exactly one such self-closing geometry, can be instantiated by direct local `use`/`xlink:href`, including forward references, integral `x`/`y` line offsets, the documented integer transform subset, and use `opacity`/`display`/`visibility` via presentation attributes, inline styles or generic stylesheet selectors (`id`/`class` are admitted for that purpose and capped at 1,024 characters). Root `display`/`visibility` propagates to each supported direct local `use` instance as well as rendered leaves. A `fill` supplied on a `use` instance is exact only for a visible, otherwise unstyled rectangle definition; explicit definition paint and all other use paint properties remain unsupported. Direct scalar presentation/layout values and inline-style property/value tokens cap at 256 characters, and transform values at 4,096; inheritable `color`, `fill`/`fill-opacity`, `display` (`inline`/`none`) and `visibility` (`visible`/`hidden`) on root and `g`/`a`; isolated root or one group `opacity`; the same supported properties through narrowly parsed inline `style` declarations, with inline precedence; bare-number/px/in/cm/mm/pt/pc dimensions implemented at 96 CSS pixels/inch; root-targeting stylesheet selectors, references to other geometry, definition-container attributes or child content, symbol viewports/styles, geometry/style root and other group attributes and relative units explicit unsupported |
| Viewport resolution | An explicit host `SvgViewport` overrides the root outer dimensions. Root `viewBox` mapping is implemented with all `meet`/`slice` alignments or `preserveAspectRatio="none"`, only when every supported rectangle edge maps to an integral output edge; other sizing and fractional-coverage cases explicit unsupported |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Synchronous/asynchronous `Memory<byte>` and writable nonseekable `Stream` destinations implemented for the current empty/rectangle subset; span-visible input/output overlap is rejected before parsing/writing, including custom memory providers; streams stay open and may be partial on failure |
| Integer rectangles | Document-order layers, bare-number/px integer coordinates, ordered lists of integer `translate()`, positive `scale()`, and axis-aligned `matrix(a 0 0 d e f)` transforms on rectangles and containers, and output-scaled geometry (only when scale preserves integral edges), SVG zero defaults for omitted coordinates/dimensions and zero `rx`/`ry`, the standard CSS named colors plus `none`/`transparent`/`currentColor`, comma or CSS Color 4 space/slash-form `rgb()`/`rgba()`/`hsl()`/`hsla()`/`#RGB`/`#RGBA`/`#RRGGBB`/`#RRGGBBAA` fill, with `fill-opacity` multiplying any color alpha, `nonzero`/`evenodd` fill rule, non-painting `stroke="none"`, and a separately bounded stroke case: untransformed integer rectangles with solid color and even integer `stroke-width` no larger than either dimension; element `opacity` is exact only for fill-less strokes, and the corresponding supported inline properties, source-over; other stroke styles and attributes explicit unsupported |
| Fractional rectangle coverage | Analytic per-pixel area coverage for integer or quarter-pixel decimal/px rectangle edges (including only mappings that preserve quarter-pixel device edges), with a calibrated fractional source-over branch; exact pinned-Chrome overlap probe admitted. Other fractional edges, fractional paths, and point-list shapes remain unsupported |
| Shapes, paths and composition | Simple closed, absolute, axis-aligned integer `polygon` elements, filled `polyline` elements (the fill closes the contour), and `path` elements using uppercase `M`/`H`/`V`/`L` commands lower to bounded non-overlapping rectangle bands (`Z` may be lowercase). Opaque untransformed horizontal or vertical `line` elements with solid even-width butt strokes, and their integral local `use` instances through the same positive integer transform subset, retain axis-aligned line geometry before lowering to a stroke band. One `g`/`a` opacity scope is isolated in a bounded temporary RGBA surface before source-over composition; nested opacity scopes are explicit unsupported pending separate calibration. Self-intersections, relative commands, repeated coordinate sets, curves, fractional coordinates, all other path grammar, clipping, masks, and shapes explicit unsupported-feature failure |
| CSS, gradients, images, filters and text | One attribute-free root-level `style` element, preceding every supported rectangle definition and rendered leaf, may contain ordered comma-separated `rect`, `g`, `#id`, `.class`, `rect.class`, `rect#id`, `g.class`, `g#id`, `[id]`, `[class]`, `[id=value]`, `[class=value]`, `g rect`, and `g > rect` selector rules using the narrowly documented declaration subset. `rect`/`g` type selectors and group combinators retain their stated scope; generic ID, class and id/class-attribute selectors apply to every currently supported shape/container carrying those attributes, including paths, point lists, lines and `use` instances; supported identifier tokens cap at 1,024 characters. Group paint/display/visibility then inherit to descendants. Per-property source order and selector specificity (`#id` > class/attribute > type selectors) are implemented. Inline declarations override stylesheet rules; stylesheet rules override presentation attributes. General selectors/cascade, stylesheet attributes, stylesheets outside the direct root position, scripts, animation and later stylesheets are unsupported. Gradients, images and filters are unsupported. Experimental direct text requires a caller-supplied prepared snapshot and direct `font-family`, optional `font-weight`/`font-style`, `font-size`, scalar `x`/`y`, scalar `text-anchor` (`start`, `middle`, `end`), and admitted paint/visibility attributes; it accepts text/CDATA only, no transform, `tspan`, stylesheet/inline font declarations, positioning lists, clipping or stroke. It is a regression-gated pipeline experiment, not admitted ordinary text or independent-browser-fidelity support |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned-memory, caller-memory, synchronous-stream and asynchronous-stream output; stream forms compress directly to at-most-64 KiB `IDAT` chunks, while memory forms retain a bounded compressed buffer; stream payloads are written in cancellation-checked chunks of at most 64 KiB |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, bounded graceful CDP shutdown with profile-cleanup retries, three byte-identical fresh-process smoke captures, and forty-three stable references |
| Fixture corpus | Forty-three original CC0 probes with verified hashes; the manifest gate renders and compares all forty-three exactly |

Stylesheet declaration/selector copies retained by rules, definition identifiers
and copied local-reference identifiers reserve their owned string and reservation-
tracker storage before the copy, then release it with parser document state.
The direct-root stylesheet reader incrementally copies text through charged
growing character storage and a charged temporary string, checking work and
cancellation before each read or copy; it releases that temporary string once
stylesheet parsing completes. Reader-provided transient attribute and inline-style
strings remain within the documented conservative reader allowance; this is not
exact BCL-allocation accounting.
Supported element attributes are validated before stylesheet matching, while the
selector matcher independently enforces the identifier cap; class-selector scans
also observe the shared parser-work limit. Each attribute walked by that policy
also consumes one work unit and checks cancellation.
Every XML-reader event is charged to that same limit, including inert metadata and
the root closing event.
Opaque-background initialization and PNG's per-pixel transparent-RGB normalization
are also charged before those scans run. Opaque-background initialization checks
cancellation every at most 4,096 pixels after allocation.
Every compressed PNG byte produced by the owned/caller-memory buffer and both
streaming IDAT sinks is charged before its copy, checksum, or output write.
The isolated Windows probe directly traverses a 64 KiB text node, a full
short-attribute root and 255 × 4 KiB attribute values below that allowance;
those observations are platform/runtime-fingerprinted regression evidence, not a
BCL or process-RSS guarantee. Each retained XML-reader report breaks managed
allocation into setup, traversal and `Name`/`Value` property-access deltas,
and counts text/CDATA nodes without retrieving their values; property access is a subset of
traversal. This makes the probe's attribution inspectable without claiming
cross-runtime exactness. Each child report records its exact input byte count and
SHA-256, which the parent verifies against the launched input. One retained
schema-2 JSON summary then groups every child report under its shared platform/runtime
fingerprint, binds the newline-normalized scenario-script source plus byte-exact
probe, renderer and fixed fixture hashes, and fails a mixed fingerprint.
The definition table reserves a two-times requested-capacity allowance before the
BCL selects its prime backing capacity, and verifies that returned capacity is
within the allowance.
Async PNG streaming reserves one small per-conversion scratch buffer before
allocation and reuses it for header and chunk-length framing, including all `IDAT`
chunks.

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
