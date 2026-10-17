# Nested clip-child intersections (A5)

The three original audit inputs retain unchanged SVG and Windows reference
bytes: two-level and three-level resource chains on a group, and the two-level
chain in pattern content. One authored control adds a transformed child map,
a clipped rectangle child and an unaffected sibling. The directory
[LICENSE](LICENSE) declares CC0 without altering original source bytes.

The [manifest](manifest.json) declares four 64×64 transparent controls. Three
original healthy stable Windows capture triples are reused; the additional
Windows and all Linux triples are frozen before correction with healthy startup,
successful cleanup and byte-identical captures. The
[study](../../../artifacts/audit-corrections-20261016/) retains the evidence.
These are focused micro-tests with no full-document/cohort count change.

[Final standalone qualification](../../../docs/SUPPORT_MATRIX.md#qualification)
is complete; the targeted results below retain this correction's checkpoint. Clipping children is distinct from
admission of `clip-path` on the `clipPath` container itself.

All four controls are exact on Windows/Linux. Targeted qualification passes
272 unit tests and 632 visual/resource cases per platform. Nested paths and
rectangle children retain their intersections and source maps. Shared native
stencils preserve color/coverage through groups, tiles, symbols and mask-source
transfers; final owners register their own content. Binding walks resource chains
iteratively with cycle detection. Material denial/cancellation/cleanup/retry and
retained-source parity controls pass. A6/F1/A7 remain pending.
