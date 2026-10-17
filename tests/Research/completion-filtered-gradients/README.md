# Filtered gradient source fidelity (A6)

Four original completion-audit sources retain their bytes and 64x64 transparent
recipes: ordinary and repeated-pattern gradient/blur failures, a solid filtered
tile and an unfiltered gradient tile. A fifth 96x64 control separates translated
zero, asymmetric and wider blurs. The directory [LICENSE](LICENSE) declares CC0
without rewriting original comments or line endings.

The [manifest](manifest.json) freezes assets/options before correction. The four
original healthy stable Windows capture triples are reused; the fifth Windows
triple and all Linux triples were captured before the shared correction, with
healthy startup, successful cleanup and identical PNGs.

The correction reuses the native padded source-bitmap bounds already retained for
image paints. Gradient dithering keeps that logical source phase when compact
capture storage omits the requested halo; shader coordinates and color arithmetic
are unchanged. The [study](../../../artifacts/audit-corrections-20261016/)
retains the source trace, rejected phase models and qualification. These are
focused micro-tests and add no full document/cohort count. All five are exact on
Windows/Linux; **371 core / 558 visual-resource checks pass per platform** in the
[targeted qualification](../../../artifacts/audit-corrections-20261016/a6-qualified-results.json).
[Final standalone qualification](../../../docs/SUPPORT_MATRIX.md#qualification)
includes this correction and the completed fixture replacement.
