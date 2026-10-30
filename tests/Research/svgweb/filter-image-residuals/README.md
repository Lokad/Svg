# Affine filter residuals

These original CC0 controls retain unresolved sampling/alpha observations separately
from the exact [empty-tile regression](../../../Fixtures/original/filter-affine-empty-tile.svg).
The shared destination-area check fixes admission; it does not fix these differences.

| Control | Independent native evidence | Remaining difference |
| --- | --- | --- |
| [Colour and opacity](affine-colour-opacity.svg) | Three healthy captures per platform, identical PNGs; [provenance](affine-colour-opacity.json). | 3,475 pixels differ; maximum alpha error 1. Review opacity, transfer and byte-store order together. |
| [Unit blur](affine-unit-blur.svg) | One healthy exploratory capture per platform; [provenance](affine-unit-blur.json). Repeated stability is unqualified. | One alpha value differs by 1. Compare the shared kernel/sampling stages with the half-unit exact control. |

Targets are research references, with no relaxed comparison or exact-admission claim.
Use the existing [capture procedure](../../../../tools/README.md); review shared causes
and preserve the targets when correcting the implementation.
