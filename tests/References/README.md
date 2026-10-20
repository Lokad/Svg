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

## Historical corporate cohort

The immutable [selection](inventory/corporate-cohort-v1.json) declares 27 documents
and 258 variants with root-only materializations. Source/license details live in
[source metadata](inventory/corporate-cohort-sources.json), the
[upstream response](inventory/corporate-cohort-upstream.json) and each document's
`sourceMetadata`. These public-domain assets retain their trademark considerations;
they are separate from the original CC0 catalog and are not package content.

The [Windows](inventory/corporate-cohort-current.json) and
[Linux](inventory/corporate-cohort-linux64.json) records retain platform outcomes
and references. This collection serves [optional measurement](../../tools/README.md#historical-corporate-cohort);
its former whole-document/percentage gates are [retired](../README.md#retired-collections).
The intended-size `inv-027` regression and its [native-route evidence](inventory/inv-027-native-route.json)
remain active. Ford keeps its original reference identities. Reusable inputs,
expected behavior and capture provenance belong here; raw diagnostics stay local.
