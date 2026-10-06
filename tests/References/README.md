Reviewed pinned-Chrome reference PNGs and their oracle metadata are stored here.
Renderer checkpoint records identify source by Git commit. Keep regression inputs,
expected images and capture provenance here; detailed run diagnostics belong under
ignored `artifacts/`. Do not store hashes of solution source, scripts, local probe
adapters, compiled binaries or packages in reference metadata: ordinary source
edits must not require metadata refreshes. Keep fixture/reference identities and
upstream oracle pins.
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

`inventory/corporate-cohort-current.json` records all 258 renderer outcomes and
all 258 references: 240 rendered comparisons (70 exact, 170 known differences)
and eighteen renderer rejections. No variants lack references. All nine variants
of YouTube, Ford, ROC HCVS and Artsy are now exact. The YouTube PNGs retain
stable healthy capture triples. `inv-027-native-route.json`
retains the byte-identical traced browser picture's raw convex flags and two
isolated diagnostic models. `CorporateCohortReferenceTests` runs every frozen
case offline, preserves original Ford references and keeps failures measurable.
Ford, ROC HCVS, Artsy and YouTube are exact in every variant (4/27). Known
differences remain measured against the original images, including increases
from float mapping and generated stroke crossings.
The capture script and VisualDiff `--cohort` command reproduce the full matrix;
see [the oracle instructions](../../docs/ORACLE.md#frozen-corporate-captures).
