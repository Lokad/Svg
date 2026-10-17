# Research probe workflow

This records the house method for calibration probes in `tests/Research/`, distilled from the batches that built the current set. It complements `docs/ORACLE.md` (capture tooling and sizing rules) and `docs/VISUAL_COMPARISON.md` (the comparator).

## Probe design

- Minimal CC0-original SVGs isolating exactly one variable (one width, one join, one stop layout). Prefer 16x16 integer-coordinate canvases; every other input stays admitted and ordinary.
- Probes target calibration frontiers (coverage, caps, joins, widths, dashes, paints, compositing), never gated syntax for its own sake: a probe that rejects for an unrelated gate answers nothing.
- ResearchProbeTests enforces the manifest mechanically: CC0 SPDX header present, license field exactly CC0-1.0, lowercase SHA-256 hashes matching working-copy bytes, oracle block complete, reference PNG dimensions matching.

## Oracle evidence

- Three fresh-process captures per probe through `tools/capture-chrome.ps1`, byte-identical all three, recorded with the pinned browser identity, recipe revision, device scale and background.
- Size captures from intrinsic dimensions per the sizing rules in `docs/ORACLE.md` (dimensioned roots crop; viewBox-only roots negotiate). Respect the 2-million-pixel transport limit.
- Name captures so runs group by probe; stage only run 1 as the reference after verifying triple stability by hash.

## Manifest entries

- Entries always record `candidateError`; entries that render with residuals additionally pin `admittedRendering` numbers, as circle-6x6 does.
- Graduate useful exact renders into the exact fixture corpus with the SVG,
  reference and complete oracle provenance retained. Remove only their research
  entry after the fixture gate covers them; do not delete the positive control.
- Compute every hash programmatically from the staged files; never retype hashes. Run the JSON validity check plus the full hash audit before testing.
- `ResearchProbeTests` re-renders every admitted entry and asserts the recorded numbers exactly, so ratchets fail loudly on drift in either direction.

## Records

- Keep `docs/RASTERIZATION_DESIGN.md` concise and current. Update it only when a lasting mechanism or constraint changes. Retain probe questions, measurements, negative results and rejected hypotheses with the local study artifacts; summarize relevant conclusions in the change description. Do not append a chronological probe log to the design document.
- Inventory batches in `docs/INVENTORY.md` are reserved for inventory dispositions; research-only turns do not append there.
