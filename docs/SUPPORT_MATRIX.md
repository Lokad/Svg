# Support matrix

## Current state

Milestone 1 (repository and contracts) is complete. Milestones 2 and 3 are in
progress. The renderer supports only the explicit subset listed below; other
visible SVG behavior fails with `UnsupportedFeature`. Nine pinned-Chrome fixtures
have exact decoded-pixel gates; this is not a general pixel-fidelity claim.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented; current rectangle raster work is preflight-bounded |
| Root SVG parsing | Bounded validation counts every descendant element; inert `id`, title/description/metadata/unused `defs`, and link containers; inheritable `color`, `fill`/`fill-opacity`, `display` (`inline`/`none`) and `visibility` (`visible`/`hidden`) on root and `g`/`a`; the same supported properties through narrowly parsed inline `style` declarations, with inline precedence; bare-number/px/in/cm/mm/pt/pc dimensions implemented at 96 CSS pixels/inch; geometry/style root and other group attributes and relative units explicit unsupported |
| Viewport resolution | Root `viewBox` mapping is implemented for explicit root dimensions with all `meet`/`slice` alignments or `preserveAspectRatio="none"`, only when every supported rectangle edge maps to an integral output edge; other sizing and fractional-coverage cases explicit unsupported |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Implemented for the current empty/rectangle subset |
| Integer rectangles | Document-order layers, bare-number/px integer coordinates and output-scaled geometry (only when scale preserves integral edges), SVG zero defaults for omitted coordinates/dimensions and zero `rx`/`ry`, SVG black default plus `none`/`transparent`/`currentColor`/`black`/`red`/`blue`/`lime`/classic comma-form `rgb()`/`rgba()`/`hsl()`/`hsla()`/`#RGB`/`#RGBA`/`#RRGGBB`/`#RRGGBBAA` fill, `nonzero`/`evenodd` fill rule, non-painting `stroke="none"`, `fill-opacity`, element `opacity`, and the corresponding supported inline properties, source-over; other attributes explicit unsupported |
| Fractional rectangle coverage | Not implemented; stable pinned-Chrome probe and investigation note recorded |
| Shapes, paths and composition | Other cases explicit unsupported-feature failure |
| CSS, gradients, images, filters and text | CSS stylesheets are not implemented; the narrowly documented inline-style subset is supported. Gradients, images, filters and text are not implemented |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned and caller destination output |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, and five stable references |
| Fixture corpus | Nine original CC0 probes with verified hashes; eight exact and one explicit unsupported result |

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
