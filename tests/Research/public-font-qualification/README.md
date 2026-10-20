# Standalone public font qualification

Four finite requests use Lokad.Svg's existing manifest, catalog, font-provider
interfaces and actual FontProbe executable. Regular, bold and intentional fallback
reuse their original SVG/options, independent metrics and exact Windows/Linux
references from the linked banks. Their assets and references are unchanged.

Only real italic rendering adds a visual control. Its unmodified static Noto Sans
Italic face 0 is pinned to the same public upstream revision as the existing Noto
assets, with the original OFL notice. The font's own italic style flag and -12-degree
post angle establish a real italic program. Two sizes/fractional origins exercise
curves/counters, AV/space pairs and composites without synthetic skew.

The italic source/options and independent FontTools cmap/hmtx/GPOS metrics were
frozen before renderer screening. Eighteen healthy stable captures cover the native
output plus missing-font and regular-program substitution controls on both platforms.
These controls prove use of the real supplied face inside the SVG image. Tests
run offline and launch the existing FontProbe, with host-owned temporary files.

This bank adds one focused control, no corpus document or API-combination quota.
Font-format/shaping limits and library ownership/resource contracts remain unchanged.
T3-public qualifies standalone use; the original four T3-private deployed-manifest
requests remain pending before production adoption. Public success makes no claim
of compatibility with that deployment. The [test guide](../../README.md#qualification) records both gates.

All four end-to-end requests pass on Windows/Linux, including exact italic pixels.
No production renderer correction, manifest conversion or package dependency was
needed. [CI](../../../.github/workflows/ci.yml) runs these requests and
package/installed-consumer checks. Private production-adoption validation remains
pending.
