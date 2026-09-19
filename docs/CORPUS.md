# Fixture corpus and provenance

`tests/Fixtures/manifest.json` is the authoritative fixed-inventory catalog.
Every asset needs a stable ID, byte hashes, provenance, rights evidence, dimensions,
feature tags, expected semantic support and an independent fidelity status.

The initial two SVGs are original CC0 probes. They have no reviewed Chrome image yet,
so their fidelity status is `oracle-failure`. That status is deliberately not a pass
and must not be changed merely because a candidate renderer produces an image.

`original-solid-red` has an admitted stable pinned-Chrome reference and one exact
candidate comparison. Its manifest records the candidate hash and zero-difference
result. A reference artifact alone is not a renderer pass; this explicit comparison
is the evidence for its `exact` status.

When a Chrome reference is admitted, record the pinned browser/archive hash, platform,
recipe revision, input/materialized hashes, resource hashes and capture options in the
manifest and `tests/References/`. References must be generated from the original bytes;
paths and text must never be rewritten to favour the candidate renderer.
