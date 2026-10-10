# Pending supplied-font documents

The next complete deliverable is the unchanged **SVG Translate textpath** by
Glrx, released under CC0 on [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:SVG_Translate_textpath.svg).
Its 704 upstream bytes, including BOM and CRLF, are retained in
`svg-translate-textpath.svg`. All text runs, positioned spans and the painted
local path remain original. The [manifest](manifest.json) declares **512×256
transparent**, with an explicit viewport for its 600×300 viewBox, before native
reference capture or candidate screening.

The original `monospace` alias and bold request resolve to the public static
Noto Sans Mono Bold face 0. [Font provenance](font-provenance.json) pins its
upstream revision and unchanged font bytes; [OFL](fonts/OFL.txt) permits retaining
the evaluation program. Product packages do not bundle it. Browser qualification
must embed this exact face inside the SVG image, retain healthy matching native
Windows/Linux triples, and prove actual font use before implementation.
The browser input must use a private embedded-family name and an explicit root
family override: unquoted CSS `monospace` selects a generic host family. That
oracle-only mapping implements the declared supplied-font alias contract; product
input bytes stay original. Negative embedding witnesses must detect fallback.

This document is **pending (0/1)**, outside the completed-document and focused-control
ledgers. Its initial Windows probe, with the declared supplied face and viewport,
rejects `tspan x` as `UnsupportedFeature`. Its positioning and typography are
within the existing finite G9 scope. Native reference/font-use qualification is
still pending; this rejection establishes the first dependency, not final fidelity.
Follow every rejection and mismatch through the whole declared output; choose
shared corrections by the document they advance. Keep unrelated combinations
visible on the group checklist. Broader online sourcing resumes after these
identified remaining support promises are completed.
