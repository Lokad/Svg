# Support matrix

## Current state

Milestone 1 (repository and contracts) is complete. Milestones 2 and 3 are in
progress. The renderer supports only the explicit subset listed below; other
visible SVG behavior fails with `UnsupportedFeature`. One pinned-Chrome fixture
has an exact decoded-pixel gate; this is not a general pixel-fidelity claim.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented |
| Root SVG parsing | Bounded root validation; inert title/description/metadata tolerated; bare-number/px dimensions implemented; geometry/style root attributes and other units explicit unsupported |
| Viewport resolution | Not implemented |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Implemented for the current empty/rectangle subset |
| Integer rectangles | Document-order layers, integer-valued numeric coordinates, `none`/`#RGB`/`#RRGGBB` fill, `fill-opacity`, source-over; other attributes explicit unsupported |
| Shapes, paths and composition | Other cases explicit unsupported-feature failure |
| CSS, gradients, images, filters and text | Not implemented |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned and caller destination output |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, and two stable references |
| Fixture corpus | Two original CC0 probes with verified hashes; one exact and one explicit unsupported result |

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
