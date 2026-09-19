# Support matrix

## Current state

Milestone 1 (repository and contracts) is complete. `SvgConverter` deliberately
reports `UnsupportedFeature` for all conversions. No SVG feature is currently
supported and no pixel-fidelity claim is made.

| Area | Status |
| --- | --- |
| Public options, limits and failure categories | Contract scaffolded |
| Root SVG parsing | Bounded root validation and dimension parsing implemented |
| Viewport resolution | Not implemented |
| Shapes, paths and composition | Not implemented |
| CSS, gradients, images, filters and text | Not implemented |
| PNG encoding | Not implemented |
| Independent RGBA comparison | Initial exact comparer implemented |
| Visual comparison CLI | Exact JSON gate implemented for bounded RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Initial launcher implemented; pinned browser and capture pending |
| Fixture corpus | Not implemented |

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
