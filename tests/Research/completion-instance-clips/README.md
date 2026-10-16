# Retained instance content clips (A3)

The seven original audit failures cover direct leaf and nested group clips in
symbols, local definition groups and markers, including an object-box marker
group. Their original SVG bytes and Windows references are unchanged. The
unclipped marker remains a separate discriminator. Two authored CC0 controls
cover forward resources, affine placement, separate instances, unaffected
siblings and clip coordinates on a retained `use`.

The [manifest](manifest.json) declares ten 64×64 transparent controls. The
directory [LICENSE](LICENSE) preserves the original source bytes without adding
comments. Eight healthy stable Windows capture triples are reused from the
audit; both additional Windows triples and all Linux triples were captured before
correction, with healthy startup, successful cleanup and byte-identical outputs.
The [study](../../../artifacts/audit-corrections-20261016/) owns declarations,
native evidence and correction diagnostics. No document/cohort count changes.

[PLAN](../../../PLAN.md) owns A3's acceptance and the remaining A4–A7 exits.

All ten controls are exact on Windows/Linux. Targeted qualification passes
370 unit tests, 781 visual/resource cases and 13 retained-effect/font checks per
platform. Clipped leaves force source replay; retained containers bind shared
native/object-box clip operations. Implicit symbol viewport clipping is retained
even without a viewBox. Material denial/cancellation/cleanup and prepared retry
cover the object-box marker and both additional controls. Full A7 qualification
remains pending after A4–A6 and F1.
