# Representative logo inventory

This document defines the versioned representative inventory required by PLAN.md R26.
It records the selection rules, record schema, legal handling, and coverage accounting
so that document sourcing can proceed without changing the rules midstream.
No selection is claimed until documents are actually inventoried below.

## Status

Framework defined 2026-09-26. Zero documents selected. Sourcing input pending.

## Purpose and non-goals

The inventory steers implementation toward ordinary, static, self-contained corporate
SVG logos, including outlined wordmarks. It does not replace the fixture corpus in
tests/Fixtures/manifest.json, which remains the authoritative fixed-inventory catalog
for admitted behavior with byte hashes and renderer verification.
Synthetic probes stay in their own count and must never stand in for real-export coverage.

## Selection rules

Documents are independently selected real export outputs, not constructed around the
admitted subset. The initial set targets 20 to 30 documents, growing toward 50 to 100.
Selection covers these archetypes, including failing cases:

- Flat curved marks and outlined wordmarks with counters and many contours.
- Holes, even-odd and nonzero winding.
- Rotations and non-uniform transforms.
- Thin strokes, hairlines, dashes and round or square caps and joins.
- Linear and radial gradients, single and multiple stops, transparency.
- Clipped and translucent combinations, groups with opacity, masks.
- Text as paths (outlined wordmarks need no fonts); live text stays out of scope.
- Small, intended and large render sizes across transparent, white and black backgrounds.

## Record schema

Each inventoried document carries a stable ID plus:

- `source`: where the export came from (tool, exporter version when known).
- `license`: rights status with evidence; restricted assets stay private (see below).
- `retrievalDate`: ISO date of acquisition.
- `archetypes`: list of archetype tags from the selection rules above.
- `variants`: size and background variants declared up front for aggregation.
- `status`: untested, exact, known-difference, rejects or unsupported with reason.
- `reference`: pinned oracle capture identity once measured, never a candidate render.

## Legal handling

Every asset needs provenance before admission. Contributor-original work follows the
corpus CC0 SPDX declaration convention. Restricted assets stay private: they are
never committed, never embedded in fixtures, and any local-only overlay directory
remains untracked. Do not simplify logos or redraw them to fit the renderer.

## Coverage accounting

Apply the R27 lessons: aggregate exactness over every declared variant, never simplify
the denominator to improve the score, and freeze the inventory and target before
any most-logos claim per S17. Entry counts must not stand in for all-variant success.
Render every entry through the manifest-style gate: asserted rejection with category
when rejected, retained reference with measured mismatch when renderable but wrong.
