# Pattern leaf clip correction (A1)

The two original 64×64 transparent completion-audit inputs are retained byte for
byte: a circle-clipped rectangle directly inside a pattern and the equivalent
clipped-group control. The directory [LICENSE](LICENSE) declares CC0 without
inserting comments into those original sources. A third CC0 control combines a
translated leaf, a forward clip definition and an unaffected sibling.

The [manifest](manifest.json) freezes the original inputs, options and platform
references before correction. The original Windows capture triples are reused;
the third Windows triple and all three Linux triples were captured in healthy
fresh processes with successful profile cleanup. Each triple is byte-identical.
The [study](../../../artifacts/audit-corrections-20261016/) retains the capture and
targeted qualification evidence.

Clipped pattern leaves now enter the existing rendered-leaf path so their clips
bind after definitions resolve, with the tile scene retaining its resources.
This reuses ordinary native clipping rather than adding pattern-specific shape
clipping. The bank contributes three controls and no document/cohort count.
Object-box pattern groups and the other completion-audit corrections remain
separate exits in [PLAN](../../../PLAN.md).

All three controls are exact on Windows/Linux. Targeted qualification passes
126 pattern unit tests and 199 visual/resource cases per platform, including
material denial, cancellation, cleanup and prepared retry for the direct input
and forward/sibling control. Full finished-batch verification remains A7.
