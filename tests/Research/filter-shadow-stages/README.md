# Filter shadow stages

These sixty inputs retain their own Windows and Linux browser images. Ten fail
before the shadow tint correction; the other fifty protect independent execution
paths. [Capture metadata](capture.json) records source/reference hashes, three
identical fresh-process captures per input, healthy startup and profile cleanup.
Browser pins and capture procedures are in [tools](../../../tools/README.md).

The controls distinguish tint/opacity quantization, retained linear-RGB conversion,
materialized blur images, one-axis blurs, ordinary versus fractional offsets,
stored inputs, `SourceAlpha`, merge/composite order and original affine sources.
Every compact/expanded or filtered/unfiltered pair keeps two independent references,
including naturally unequal outputs. Graph expansion is not an exactness oracle.

The [rendering tests](../../Lokad.Svg.VisualTests/SvgFilterStageReferenceTests.cs)
use canonical synchronous conversion. Direct tint mechanisms and the existing
representative API/ownership contracts cover their separate responsibilities;
these images do not repeat the API matrix. These bounded controls do not establish
general equivalence of affine shadow graphs.

The [opacity and sampling controls](../filter-opacity-sampling/README.md) add
independent use/symbol/pattern consumers. Two scaled compact shadows remain
routine witnesses in the [uniform bank](../filter-uniform-precision/README.md);
its historical captures and current resolution status stay separate.
