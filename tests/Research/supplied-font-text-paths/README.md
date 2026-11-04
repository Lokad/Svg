# supplied-font-text-paths regression coverage

Seven [controls](manifest.json) retain canonical conversion and exact Windows/Linux
references. Forward xlink resolution and a glyph suppressed by a short path also
exercise the twelve output forms routinely. The separate font-contract fixture
keeps its existing exclusion from this rendering selection.
See the [responsibility map](../release-baselines/font-output-responsibilities.md).
