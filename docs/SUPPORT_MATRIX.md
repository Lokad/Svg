# Support matrix

## Current state

Milestone 1 (repository and contracts) is complete. `SvgConverter` deliberately
reports `UnsupportedFeature` for all conversions. No SVG feature is currently
supported and no pixel-fidelity claim is made.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Contract scaffolded |
| Root SVG parsing | Bounded root validation; inert title/description/metadata tolerated; bare-number/px dimensions implemented; geometry/style root attributes and other units explicit unsupported |
| Viewport resolution | Not implemented |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Implemented for the current empty/rectangle subset |
| Integer rectangles | Document-order layers, integer-valued numeric coordinates, `#RGB`/`#RRGGBB` fill, `fill-opacity`, source-over; other attributes explicit unsupported |
| Shapes, paths and composition | Other cases explicit unsupported-feature failure |
| CSS, gradients, images, filters and text | Not implemented |
| PNG encoding | Not implemented |
| Independent RGBA comparison | Initial exact comparer implemented |
| Visual comparison CLI | Exact JSON gate implemented for bounded RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Initial launcher/capture and a hash-pinned Windows CFT archive; reviewed references pending |
| Fixture corpus | Not implemented |

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
