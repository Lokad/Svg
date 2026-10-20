# Frozen references

This directory holds reviewed pinned-Chrome PNGs and oracle metadata. Asset hashes
identify inputs, font resources, browser pins and expected images; Git identifies
renderer/tool code. Ordinary source edits must not require reference metadata
refreshes. Source snippets, local adapters, binaries and diagnostic reports need
no extra identity hashes.

[Fixture admission](../README.md#fixture-admission) defines provenance requirements;
[capture and comparison](../../tools/README.md) defines the tooling. Each reference
keeps the recipe that produced it. Do not mass-update recipe revisions or replace
pixels to accommodate renderer output.

## Retained export evidence

The former corporate sweep, capture command and aggregate measurements live in
Git history; its document-count and percentage gates are [retired](../README.md#retired-collections).
[Export sources](inventory/export-sources.json), the original
[source metadata](inventory/corporate-cohort-sources.json) and
[upstream response](inventory/corporate-cohort-upstream.json) retain source/license
facts independently of the retired selection. These public-domain exports retain
their trademark considerations and are not package content.

The intended-size `inv-027` regression keeps its [capture contract](inventory/inv-027-reference.json)
and [native-route evidence](inventory/inv-027-native-route.json). Ford retains its
original [variant references](inventory/inv-002-variants.json). Other retained
native witnesses keep their [capture contracts](inventory/retained-export-references.json).
Unconsumed cohort-only PNGs and root materializations are retired with the sweep.
Reusable inputs, expected behavior and capture provenance belong here; raw
diagnostics stay local.
