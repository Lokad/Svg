Reviewed pinned-Chrome reference PNGs and their oracle metadata are stored here.
The metadata records the exact capture recipe used for each reference. Revision 1
is retained historical provenance; revision 2 added an RGBA-transparent
background before capture. Revision 3 also configures the blank target before
navigating to the data-URL page. Do not mass-update a reference's recipe revision:
baseline changes require explicit recapture and review.

## Frozen corporate cohort

`inventory/corporate-cohort-v1.json` declares 27 independently sourced brand and
organization documents / 258 variants before fresh candidate comparison. The
selection, upstream API response and source metadata are adjacent; original
sources remain under `tests/Fixtures/candidates/`. Root-only materializations
live in `inventory/cohort-inputs/`, separately hashed for renderer and oracle.
Ford retains its original nine references. These third-party public-domain
assets never enter the CC0 fixture manifest or the package. Membership/geometry
invariant tests run offline; new references require stable healthy capture
triples. Measurement records are separate from the immutable selection/options.
See [the inventory](../../docs/INVENTORY.md#frozen-corporate-cohort-2026-10-05).
