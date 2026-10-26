# Text-node fragment metrics

The [capture](capture.json) binds each SVG, supplied [Noto Sans font](../supplied-font-labels/README.md)
and independent Windows/Linux image to three healthy, stable Chrome launches.
Cases cover comments, descriptions, CDATA, empty and ordinary spans, kerning,
ligatures, long reader chunks, hidden paint returns, resource replay and fitting.
Text nodes retain fragment width rounding while shaping continues across nodes.

Two fitting inputs retain an existing three-pixel Windows difference separately
from the exact cases. Their native images are preserved; both inputs are exact
on Linux. No image tolerance or reference is changed to hide that difference.
