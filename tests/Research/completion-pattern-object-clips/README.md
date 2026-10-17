# Object-box pattern content clips (A2)

This bank retains the three original completion-audit inputs byte for byte:
object-box clipping on a pattern leaf, a vector group and an image group. The
directory [LICENSE](LICENSE) declares CC0 without rewriting the original sources.
One additional CC0 control combines transformed multi-child group bounds, a
zero-width stroked target and an unaffected sibling.

The [manifest](manifest.json) freezes all four 64×64 transparent outputs before
correction. Two existing healthy Windows triples are reused; the leaf's fresh
triple also matches its original single screenshot. New Windows/Linux triples
have healthy startup, successful profile cleanup and byte-identical PNGs. The
[study](../../../artifacts/audit-corrections-20261016/) retains the declarations,
captures and correction evidence. These are four controls, with no document or
cohort count change.

A1 already closes the leaf case. Pattern groups now collect the complete
pre-clip target with the existing object-box bounds collector, then bind through
ordinary native clipping. Bounds lookup and clip registration both use the tile
scene owner, including recorded image bounds. User-space groups keep their
existing route. [Final standalone qualification](../../../docs/SUPPORT_MATRIX.md#current-standalone-qualification-2026-10-17)
is complete; the targeted results below retain this correction's checkpoint.

All four controls are exact on Windows/Linux. Targeted qualification passes
159 pattern/object-box/scope unit tests and 217 visual/resource cases per platform, including
material denial, cancellation, cleanup and prepared retry for the image input
and transformed/degenerate target control. Full finished-batch verification remains A7.
