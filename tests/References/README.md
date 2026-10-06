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

## Current inactive-presentation checkpoint

The bank has 1,168 fixtures (961 exact, 183 known, 24 rejected), 36 research
probes (twelve exact, 23 known, one rejected) and 92 legacy Default cases:
1,296 executed cases. Six independent controls add six exact comparisons.
All prior renders/dispositions and 955 prior exact fixture/twelve exact research
PNGs remain. One active-marker rejection reports a more precise value diagnostic.
Eighteen fresh captures are stable, healthy and cleaned. Target is exact in all
nine original corporate variants; all other 249 corporate records and original
reference PNGs remain unchanged. Totals are 143 exact / 115 known / zero rejected,
10/27 fully exact documents. Detailed runs stay under ignored
`artifacts/inert-presentation-20261006/`.

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
all 258 references: 258 rendered comparisons (143 exact, 115 known differences)
and zero renderer rejections. No variants lack references. All nine variants
of YouTube, Ford, ROC HCVS, Artsy and NASA remain exact. SimpleX, Omega and Google
are now exact in all twelve/twelve/nine variants respectively. The YouTube PNGs retain
stable healthy capture triples. `inv-027-native-route.json`
retains the byte-identical traced browser picture's raw convex flags and two
isolated diagnostic models. `CorporateCohortReferenceTests` runs every frozen
case offline, preserves original Ford references and keeps failures measurable.
Ten documents are exact in every frozen variant (10/27), including IKEA and Target. Known
differences remain measured against the original images. The earlier native open-stroke
construction improved all nine Rede Manchete variants, with no prior mismatch
count increase and all 70 exact corporate PNGs preserved. Six original research
miter/bevel probes graduate with unchanged SVG/PNG/provenance; three existing
viewBox configurations and nine supplemental controls become exact. Three new
square-cap/bitmap-bound references remain known differences. Their controls were
declared after the production edit; all 36 fresh captures are stable and healthy.
Detailed experiments stay under ignored `artifacts/stroke-construction-20261006/`.
Source changes alone require no reference refresh.
The capture script and VisualDiff `--cohort` command reproduce the full matrix;
see [the oracle instructions](../../docs/ORACLE.md#frozen-corporate-captures).

Per-pixel witness dumps belong in ignored run diagnostics. Current cohort
records retain candidate identity and comparison summaries, so code edits that
change a few pixels do not rewrite large debug dumps in test data.
