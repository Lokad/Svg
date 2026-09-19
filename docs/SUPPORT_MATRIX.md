# Support matrix

## Current state

Milestone 1 (repository and contracts) is complete. Milestones 2 and 3 are in
progress. The renderer supports only the explicit subset listed below; other
visible SVG behavior fails with `UnsupportedFeature`. Three pinned-Chrome fixtures
have exact decoded-pixel gates; this is not a general pixel-fidelity claim.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented |
| Root SVG parsing | Bounded root validation; inheritable `fill`/`fill-opacity`, `display` (`inline`/`none`) and `visibility` (`visible`/`hidden`) on root and `g`, inert title/description/metadata; bare-number/px dimensions implemented; geometry/style root and other group attributes and other units explicit unsupported |
| Viewport resolution | Not implemented |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Implemented for the current empty/rectangle subset |
| Integer rectangles | Document-order layers, integer-valued numeric coordinates, SVG black default plus `none`/`transparent`/`black`/`red`/`blue`/`#RGB`/`#RGBA`/`#RRGGBB`/`#RRGGBBAA` fill, `fill-opacity`, element `opacity`, source-over; other attributes explicit unsupported |
| Fractional rectangle coverage | Not implemented; stable pinned-Chrome probe and investigation note recorded |
| Shapes, paths and composition | Other cases explicit unsupported-feature failure |
| CSS, gradients, images, filters and text | Not implemented |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned and caller destination output |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, and four stable references |
| Fixture corpus | Four original CC0 probes with verified hashes; three exact and one explicit unsupported result |

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
