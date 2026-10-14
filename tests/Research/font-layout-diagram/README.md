# Supplied-font layout guide

This retained CC0 document has one declared 432×320 transparent output, with a
white background painted by its original SVG. It is **0/1 complete**. Its four
separate position/rotation, textLength, baseline and span-font controls are
**0/4 admitted**. Current categorized gates are in [the manifest](manifest.json).
Neither rejection nor reference capture counts as document completion.

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

Complete every rejection and pixel mismatch against these frozen references.
Use the four controls to diagnose shared layout fixes; the transformed curved
label and out-of-range run remain part of the whole document. Existing curved
path witnesses protect already supported semantics. Keep the corporate cohort,
selected development documents and five completed authored documents unchanged.
Wider finite G9 and the separate six-pixel blur correction stay required.

The capture/declaration checkpoint passes **7,588 tests/platform**, preserving
every preceding case. Routine qualification takes about **1m51s Windows / 5m18s
Linux**; the [existing receipt](../../../artifacts/maintenance-review-20261012/font-layout-declaration-qualified-results.json)
records the pending document/control outcomes separately. Native captures and
intended-font witnesses do not count as passing renderer outputs.
