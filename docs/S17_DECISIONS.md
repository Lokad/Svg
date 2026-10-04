# S17 release decisions (user-owned)

Drafted 2026-10-04 at master 11fdc143. Implementation work in rows 1-7 is complete within the documented architectural limits (convex fast path cannot advance live state; curved-stroke separation falsified; skew and general-matrix admissions paused). The three decisions below block any most-documents coverage claim. They block no implementation slice meanwhile.

Verified floors on this tree: fixture census 431 entries = 350 exact + 81 unsupported across 280 distinct documents (tools/census-report.ps1); research corpus 72 probes; gates 2849 core + 100 visual + 6 font green; inventory sweep 88 render + 4 rejects with zero disposition flips.

## Q1 - numeric document target (what most means)

Pick the fraction, the set, and the measurement: all-variant exact over the frozen inventory, exact-entry fraction of the fixture corpus, or tiered targets per stratum (wordmarks, marks, flags). Caution from PLAN section 1: test counts are not image counts, and only completed all-variant evidence (small/intended/large x transparent/white/black, e.g. the inv-029, inv-034, inv-053 class) counts toward a rate; dated 27-of-92-style tallies must not be reused as the rate.

## Q2 - frozen corporate cohort

Freeze the 92 files under tests/Fixtures/candidates/ with the R26 selection rules in docs/INVENTORY.md as the corporate cohort, or name the strata to add first. Freezing also fixes the variant recipe and the oracle refresh policy for new captures.

## Q3 - quirk budget for parked oracle-side families

Per family: accept the pixels as documented behavior, fund backend-spec work to match Chrome exactly, or adjust the Q1 target around them.

- Slanted-band over-coverage (about +23 uniform; our walker reads exact area): DESIGN band-interval and slanted-row entries.
- Transversal and arc-fringe over-coverage: DESIGN round-join, miter and arc-fringe entries.
- Twin cap-tip slivers (exact 9.8; ours 4-6; oracle 0): DESIGN twin cap-tip three-way entry.
- Ford flank straddles (7 over-40 pixels; mean error about 15 ours versus about 28 oracle; zero interior): DESIGN Ford census and over-40 entries.
- Tessellation phase on radial discs, snap-phase straddle, grouped-gradient compositing, composite dither phase: respective DESIGN entries.

Reply with the Q1 definition, the Q2 freeze, and per-family Q3 dispositions; implementation resumes on that answer.
