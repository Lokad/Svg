Reviewed pinned-Chrome reference PNGs and their oracle metadata are stored here.
Renderer checkpoint records identify source by Git commit. Keep regression inputs,
expected images and capture provenance here; detailed run diagnostics belong under
ignored `artifacts/`. Identify upstream code with pinned revisions and URLs.
Do not store hashes of source files, extracted code snippets, scripts, local probe
adapters, solution binaries, packages, diagnostic reports or traces in reference
metadata. Ordinary source edits must not require metadata refreshes. Keep
fixture/reference identities and upstream oracle pins.
Regression records contain reusable inputs, expected behavior and oracle
provenance. Keep raw run reports, resource ledgers and timing batches under
ignored artifacts; retain useful summaries in the existing documentation.
The metadata records the exact capture recipe used for each reference. Revision 1
is retained historical provenance; revision 2 added an RGBA-transparent
background before capture. Revision 3 also configures the blank target before
navigating to the data-URL page. Do not mass-update a reference's recipe revision:
baseline changes require explicit recapture and review.

## Current retained-curve root-mapping checkpoint

Final curve controls and endpoints use native float multiply/add operations.
NASA's small variants become exact and all nine frozen variants now pass. The
bank has 1,042 fixtures (847 exact, 165 known, 30 rejected) and 36 research probes.
All 1,033 prior fixture outputs, 36 research outcomes and 70 exact corporate PNGs
are unchanged. Nine fresh configurations retain three exact cubic-slice, three
known cubic-meet and three rejected anisotropic quadratic outcomes. Their 27
fresh captures are stable and healthy; controls were declared after the edit
and before capture/comparison. Original images stay frozen. Known large SDL,
SG Rotation, Party of Changes and Pepsi variants worsen; the width-one cap
regression remains unchanged. Source edits alone require no metadata refresh.

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
all 258 references: 240 rendered comparisons (73 exact, 167 known differences)
and eighteen renderer rejections. No variants lack references. All nine variants
of YouTube, Ford, ROC HCVS, Artsy and NASA are now exact. The YouTube PNGs retain
stable healthy capture triples. `inv-027-native-route.json`
retains the byte-identical traced browser picture's raw convex flags and two
isolated diagnostic models. `CorporateCohortReferenceTests` runs every frozen
case offline, preserves original Ford references and keeps failures measurable.
Ford, ROC HCVS, Artsy, YouTube and NASA are exact in every variant (5/27). Known
differences remain measured against the original images. At the earlier native open-stroke
construction improves all nine Rede Manchete variants, with no prior mismatch
count increase and all 70 exact corporate PNGs preserved. Six original research
miter/bevel probes graduate with unchanged SVG/PNG/provenance; three existing
viewBox configurations and nine supplemental controls become exact. Three new
square-cap/bitmap-bound references remain known differences. Their controls were
declared after the production edit; all 36 fresh captures are stable and healthy.
Detailed experiments stay under ignored `artifacts/stroke-construction-20261006/`.
Source changes alone require no reference refresh.
The capture script and VisualDiff `--cohort` command reproduce the full matrix;
see [the oracle instructions](../../docs/ORACLE.md#frozen-corporate-captures).
