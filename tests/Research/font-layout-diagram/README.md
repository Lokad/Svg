# Supplied-font layout guide

This retained CC0 document has one declared 432×320 transparent output, with a
white background painted by its original SVG. It is **1/1 exact** on Windows and
Linux. Its four distinct position/rotation, textLength, baseline and span-font
controls are separately **4/4 exact** against the unchanged platform references
in [the manifest](manifest.json).

Original SVGs, options and healthy independent Windows/Linux capture triples
freeze before layout correction. Each platform keeps its own native pixels.
Programs reuse the unchanged public standalone static Noto Sans Regular and
Noto Sans Mono Bold face 0 assets, with their existing provenance and OFL notices.
The caller catalog supplies Example/Mono and falls back from Missing to Example.
No program is converted, pruned, discovered from the host or bundled in product.

Browser-only font embedding places those exact TTF bytes inside the SVG image.
Missing maps to Example in browser CSS to reproduce the declared catalog fallback;
Mono retains weight 700. [Missing-face witnesses](font-use-proof/qualification.json)
prove that removing either supplied program changes the whole image on each
platform. The original SVG and converter font contract remain unchanged.

The two-program payload exceeds Chromium's 2 MiB navigation URL limit after nested
base64 encoding. The existing capture tool transports large inputs through an
in-memory Blob in the same image element, preserving byte content, sandbox,
blocked external resources, size limits and health/cleanup checks. The small
data-URL recipe is unchanged. Independent baseline captures with inert padding
prove identical glyph pixels across both transports; the existing native smoke
also checks three large-input captures against its unchanged red reference.

The shared correction retains scalar-aware root position/rotation lists, both
textLength adjustments, supplied-height baselines and inherited per-span faces
and sizes. Normalized text intervals share one continuous run and anchor width;
font discovery acquires only the metrics needed by painted containing runs.
Windows fits stretched glyphs with independently rounded horizontal/vertical
scales; reduced quadratic scans stay within their monotonic endpoint span.
Linux light fitting uses the supplied standard stem widths for its segment links.
The [stem vector](diagnostics/mono-stem-contours.json) and
[complete curved-counter mask](diagnostics/windows-curve-span.json) protect the
independent native observations without a native dependency in tests or product.

The transformed curved label and out-of-range run are exact in the whole guide.
A synthetic repeated text-path regression protects per-glyph mixed sizes.
This sixth completed authored document is separate from the frozen corporate
cohort and selected 50 documents. No SVG, option, font or reference byte changes.
Nested-span list/rotation/length propagation, length adjustment on paths,
word spacing/decorations and remaining composite/stroke/path semantics remain
finite G9 work; this diagram does not close that group. The independent stretched
accent diagnostic retains a one-pixel native-mask difference to investigate in
that remaining work, without waiving any established document result.

Finished-correction Release qualification passes **7,608 tests/platform**
(5,239 core / 2,363 visual / six FontProbe), with every prior case retained and
original-input assertion migrations explicitly audited. Routine build/suite
cost is approximately **1m40s Windows / 5m36s Linux**. Evidence is recorded in the
[existing receipt](../../../artifacts/maintenance-review-20261012/font-layout-guide-qualified-results.json).
The preceding declaration/capture receipt remains historical evidence; intended-font
witnesses alone do not count as passing renderer outputs.
