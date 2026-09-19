# Support matrix

## Current state

Milestone 1 (repository and contracts) is complete. Milestones 2 and 3 are in
progress. The renderer supports only the explicit subset listed below; other
visible SVG behavior fails with `UnsupportedFeature`. Twenty-eight pinned-Chrome fixtures
have exact decoded-pixel gates; this is not a general pixel-fidelity claim.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented; current rectangle raster work is preflight-bounded |
| Root SVG parsing | Bounded validation counts every descendant element; inert `id`, title/description/metadata/unused `defs`, and link containers; inheritable `color`, `fill`/`fill-opacity`, `display` (`inline`/`none`) and `visibility` (`visible`/`hidden`) on root and `g`/`a`; isolated root or one group `opacity`; the same supported properties through narrowly parsed inline `style` declarations, with inline precedence; bare-number/px/in/cm/mm/pt/pc dimensions implemented at 96 CSS pixels/inch; geometry/style root and other group attributes and relative units explicit unsupported |
| Viewport resolution | An explicit host `SvgViewport` overrides the root outer dimensions. Root `viewBox` mapping is implemented with all `meet`/`slice` alignments or `preserveAspectRatio="none"`, only when every supported rectangle edge maps to an integral output edge; other sizing and fractional-coverage cases explicit unsupported |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Implemented for the current empty/rectangle subset |
| Integer rectangles | Document-order layers, bare-number/px integer coordinates, ordered lists of integer `translate()`, positive `scale()`, and axis-aligned `matrix(a 0 0 d e f)` transforms on rectangles and containers, and output-scaled geometry (only when scale preserves integral edges), SVG zero defaults for omitted coordinates/dimensions and zero `rx`/`ry`, SVG black default plus `none`/`transparent`/`currentColor` and the basic CSS named palette, classic comma-form `rgb()`/`rgba()`/`hsl()`/`hsla()`/`#RGB`/`#RGBA`/`#RRGGBB`/`#RRGGBBAA` fill, with `fill-opacity` multiplying any color alpha, `nonzero`/`evenodd` fill rule, non-painting `stroke="none"`, element `opacity`, and the corresponding supported inline properties, source-over; other attributes explicit unsupported |
| Fractional rectangle coverage | Analytic per-pixel area coverage for integer or quarter-pixel decimal/px rectangle edges (including only mappings that preserve quarter-pixel device edges), with a calibrated fractional source-over branch; exact pinned-Chrome overlap probe admitted. Other fractional edges, fractional paths, and point-list shapes remain unsupported |
| Shapes, paths and composition | Simple closed, absolute, axis-aligned integer `polygon` elements, filled `polyline` elements (the fill closes the contour), and `path` elements using uppercase `M`/`H`/`V`/`L` commands lower to bounded non-overlapping rectangle bands (`Z` may be lowercase). One `g`/`a` opacity scope is isolated in a bounded temporary RGBA surface before source-over composition; nested opacity scopes are explicit unsupported pending separate calibration. Self-intersections, relative commands, repeated coordinate sets, curves, fractional coordinates, all other path grammar, clipping, masks, and shapes explicit unsupported-feature failure |
| CSS, gradients, images, filters and text | One attribute-free root-level `style` element, preceding all rendered content, may contain ordered `rect`, `#id`, `.class`, `g rect`, and `g > rect` selector rules using the narrowly documented declaration subset; matching is limited to rectangles. Per-property source order and selector specificity (`#id` > `.class` > type selectors) are implemented. Inline declarations override stylesheet rules; stylesheet rules override presentation attributes. General selectors/cascade, stylesheet inheritance, stylesheet attributes and later stylesheets are unsupported. Gradients, images, filters and text are not implemented |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned and caller destination output |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, and twenty-eight stable references |
| Fixture corpus | Twenty-eight original CC0 probes with verified hashes; all twenty-eight exact |

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
