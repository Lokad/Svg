# Representative logo inventory

This document defines the versioned representative inventory required by PLAN.md R26.
It records the selection rules, record schema, legal handling, and coverage accounting
so that document sourcing can proceed without changing the rules midstream.
No selection is claimed until documents are actually inventoried below.

## Status

Framework defined 2026-09-26. Batch 1 selected 2026-09-26: 5 documents (inv-001..inv-005). Renderer probe 2026-09-26 (Release, SvgRenderOptions.Default): all five reject with categorized UnsupportedFeature reasons below; no candidate renders. Oracle triples stable for batch 1 (3/3 byte-identical each; per-entry hashes below). Batch 2 selected 2026-09-26: 5 documents (inv-006..inv-010), all renderer-probed 2026-09-26; oracle triples stable for batch 2 (3/3 byte-identical each; per-entry hashes below). Batch 3 selected 2026-09-26: 2 documents (inv-011..inv-012), both renderer-probed 2026-09-26; oracle triples stable for batch 3 (3/3 byte-identical each; per-entry hashes below). Batch 4 selected 2026-09-26: 2 documents (inv-013..inv-014), both renderer-probed 2026-09-26; oracle triples stable for batch 4 (3/3 byte-identical each; per-entry hashes below). Batch 5 selected 2026-09-26: 2 documents (inv-015..inv-016), both renderer-probed 2026-09-26; oracle triples pending. A staged USGov candidate (Joint Typhoon Warning Center logo, PD, pt units, feGaussianBlur filter) was excluded: it carries live text elements, which the selection rules keep out of scope (fonts); filter primitives remain a genuine gap. A third staged file (4-set Venn, PD) was held back as a redundant fourth DTD rejection; it can backfill later if the rejects path needs volume.

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

## Selected documents (batch 1, 2026-09-26)

Public-domain logo SVGs sourced from Wikimedia Commons (all LicenseShortName=Public domain per Commons API extmetadata, 2026-09-26; no Trademarked template on file pages; trusted revisions). Commons PD tags state copyright status; depicted marks may carry trademark considerations, recorded here rather than hidden. Files live in tests/Fixtures/candidates/ with provenance; third-party files carry no CC0 SPDX header and are never copied into tests/Fixtures/original/. Repo policy (*.svg text eol=lf) normalizes line endings on commit: recorded hashes identify the canonical LF blob bytes, which is what any fresh checkout materializes. (XML parsing normalizes line endings before parsing, so oracle captures and renderer probes are unaffected.)

### inv-001 — SimpleLogin logo

- file: tests/Fixtures/candidates/inv-001.svg (SHA-256 BFF82F97D7A113BA10B4DEF3341BD2D5FD43FF3E5BAD648157708F27A1BFDD6B)
- source: Wikimedia Commons, File:SimpleLogin logo. Retrieval 2026-09-26.
- license: PD-logo (simplelogin.io/press via Commons).
- retrievalDate: 2026-09-26.
- archetypes: flat mark, linear gradient (2 stops).
- content: 8 paths.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature; first blocker: Only integer rectangle coordinates are implemented).
- reference: none yet. [inv-001 only: exploratory oracle triple 2026-09-26, 59x58 transparent, 3/3 byte-identical SHA-256 cc74b32530f588d8c5bf2f708250a826024163c74f978fe1536309d2a848d294, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-002 — Ford simple text logo

- file: tests/Fixtures/candidates/inv-002.svg (SHA-256 67459A7C7D446E6CD54C8B4ECA5A994908EC2D9A51F75406BF669B810D905F84)
- source: Wikimedia Commons, File:Ford simple text logo. Retrieval 2026-09-26.
- license: PD-textlogo (ford.es via Commons).
- retrievalDate: 2026-09-26.
- archetypes: text-as-paths outlined wordmark.
- content: 2 paths, inkscape metadata.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: SVG root attribute x is not implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 500x200 transparent, 3/3 byte-identical SHA-256 723f76eea2ff165f6766b5dd1e6b199d904ebca8c392f12ae61ac881e7c8d3d7, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-003 — SDL logo

- file: tests/Fixtures/candidates/inv-003.svg (SHA-256 A0B8CFEFBF49BE3B5D2E25F06F7592EF6249FA5D225A9843FDD37FF5C7450F1F)
- source: Wikimedia Commons, File:SDL logo. Retrieval 2026-09-26.
- license: PD-textlogo (vectorised by uploader via Commons).
- retrievalDate: 2026-09-26.
- archetypes: flat mark, radial gradient.
- content: 4 paths.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only positive axis-aligned matrix() gradient transforms are implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 738x371 transparent, 3/3 byte-identical SHA-256 a941550faea36541a0656296ba5b8ece0cf0cc947311a0ebff739fa32cb90161, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-004 — UAAR logo 2012

- file: tests/Fixtures/candidates/inv-004.svg (SHA-256 BD196823376965820189695EE07A6EC6BCB792564653EF1D66BAD5B2142DC5F5)
- source: Wikimedia Commons, File:UAAR logo 2012. Retrieval 2026-09-26.
- license: PD-TextLogo (vectorialized from PNG via Commons).
- retrievalDate: 2026-09-26.
- archetypes: path mark.
- content: 6 paths, inkscape metadata.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: SVG root attribute x is not implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 595x740 transparent, 3/3 byte-identical SHA-256 7db3cdfce75d6906ddd36dca06bb8e5b52102a5a5701c677f734194adbc303d9, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-005 — SimpleX.chat logo

- file: tests/Fixtures/candidates/inv-005.svg (SHA-256 1F631699B6559197BF7A1163546388DD095F84E2B2E77A47A4DC2703B6C628B6)
- source: Wikimedia Commons, File:SimpleX.chat logo. Retrieval 2026-09-26.
- license: PD-text-logo (github simplex-chat via Commons).
- retrievalDate: 2026-09-26.
- archetypes: clipped composition.
- content: 3 paths, clipPath, rect.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Path attribute clip-rule is not implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 815x233 transparent, 3/3 byte-identical SHA-256 d6bf022775737d415fd76ce5e40c5022d0c792c05c9188ed3e779f0135201d58, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

## Selected documents (batch 2, 2026-09-26)

Public-domain SVGs sourced from Wikimedia Commons (all LicenseShortName=Public domain per Commons API extmetadata, 2026-09-26). Same trademark disclosure as batch 1: Commons PD tags state copyright status; depicted marks may carry trademark considerations. Batch 2 targets the archetypes batch 1 missed: strokes, dashes, cap degeneracy, transforms without viewBox, physical units, and the DTD security-rejection path. Files live in tests/Fixtures/candidates/ with provenance; no CC0 SPDX headers; never copied into tests/Fixtures/original/.

### inv-006 — Omega logo

- file: tests/Fixtures/candidates/inv-006.svg (SHA-256 4A921B949C4DFDA7A885B27D1A4EB693BDACB33ECC7A18BC41C9B9E985938588)
- source: Wikimedia Commons, File:Omega logo.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: physical mm units, flat mark, defs container.
- content: 2 paths, sodipodi metadata.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Definition container attributes are not implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 57x23 transparent, 3/3 byte-identical SHA-256 8dd0e9e4f9b990e7a17e36658c411cecd4bbf37fffa84294ca2babee2a9ec534, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-007 — Zeppelin logo

- file: tests/Fixtures/candidates/inv-007.svg (SHA-256 DD9881FB39562630E4C13C97D8CF78C905F456D5461C77DF8B18C91DD521658C)
- source: Wikimedia Commons, File:Zeppelin logo.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: transforms without viewBox.
- content: 4 paths, 3 transforms, DOCTYPE present.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: rejects (rejects:MalformedInput: DTD prohibited by XML security policy).
- reference: none yet. [exploratory oracle triple 2026-09-26, 500x143 transparent, 3/3 byte-identical SHA-256 aabc201800a4fa1aaa7d187667252c2a0e978664633566dd2210302f19dcb8d3, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-008 — Eisenbahn und Häfen logo

- file: tests/Fixtures/candidates/inv-008.svg (SHA-256 BBB81E14EAB6C57EC3876C4A7E116FD38351F94B94A0A5BDE954186A878B2DE3)
- source: Wikimedia Commons, File:Eisenbahn und Häfen logo.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: stroked axis-aligned polyline.
- content: 1 path, stroke-width 2.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only closed axis-aligned integer paths are implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 72x72 transparent, 3/3 byte-identical SHA-256 8808db0d924ba280fa602b09e7c07c6f304d43d3dcd1826f78c80da42f1ea312, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-009 — Rede Manchete

- file: tests/Fixtures/candidates/inv-009.svg (SHA-256 C8882446B5C341A41B0848187F375868AE4C94CC56EFB7A2CFD5C5998F80971E)
- source: Wikimedia Commons, File:Rede Manchete.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: strokes, round caps, degenerate zero-length segments.
- content: 3 paths, stroke-widths 36/98, stroke-linecap round.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only non-painting stroke none is implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 500x500 transparent, 3/3 byte-identical SHA-256 2cd201a0d5140bbd1ef7f4379195f771494228db340779af0c411f96edc60ded, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-010 — ROC HCVS logo

- file: tests/Fixtures/candidates/inv-010.svg (SHA-256 6BE57E308A793EEEAD287B1601D31C95A1862DE7FA9834625BC7C0A222D199A7)
- source: Wikimedia Commons, File:ROC HCVS logo.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: dashed stroke.
- content: 1 path, stroke-width 56, stroke-dasharray 56,8.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only closed axis-aligned integer paths are implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 200x200 transparent, 3/3 byte-identical SHA-256 c7d0d6be3c4aa195fdb9e58ae84603d5bd5fd057587a05b4630c5d4804b4f1c1, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

## Selected documents (batch 3, 2026-09-26)

Public-domain SVGs sourced from Wikimedia Commons (all LicenseShortName=Public domain per Commons API extmetadata, 2026-09-26). Same trademark disclosure as batches 1-2. Batch 3 adds a viewBox-only viewport and a many-contour wordmark with an ellipse primitive (unrenderable here: second DTD security rejection). Cross-inventory live-attribute census 2026-09-26 (parsed attributes, not metadata text): the only live fill-rule evenodd is inv-005 (3 paths); inv-003/inv-007 evenodd strings sit in gradient-stop/CSS style text, not live attributes. The opacity strings in inv-002/inv-004/inv-006 are Inkscape borderopacity/pageopacity document metadata, not rendering opacity. Confirmed genuine gaps: group/paint opacity, mask elements, rotations, renderable ellipse/circle/rect-with-geometry. Files live in tests/Fixtures/candidates/ with provenance; no CC0 SPDX headers; never copied into tests/Fixtures/original/.

### inv-011 — Logo of Indonesian Red Cross

- file: tests/Fixtures/candidates/inv-011.svg (SHA-256 2BE4865EAAC5F4D128A6CF60DCC4F4F05E77939F96CDA398EBF190E3F3190570)
- source: Wikimedia Commons, File:Logo of Indonesian Red Cross.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: viewBox-only viewport (no width/height), fractional coordinates.
- content: 3 paths.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only integer path coordinates are implemented; fractional and scientific notation stay unsupported).
- reference: none yet. [exploratory oracle triple 2026-09-26, 512x500 transparent, 3/3 byte-identical SHA-256 44f537838d608edfcbc3e3291482ef2484d82e8c759b47354317fb3708197849, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-012 — SG Rotation Leipzig

- file: tests/Fixtures/candidates/inv-012.svg (SHA-256 4DFF5554049C7ACD23A96F57ABE7E40D5D6651B52B6A4C21A2E0FB586548D7B2)
- source: Wikimedia Commons, File:SG Rotation Leipzig.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: many-contour wordmark, ellipse primitive, px units (unrenderable: DOCTYPE present).
- content: 26 paths, 1 ellipse, 6 groups.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: rejects (rejects:MalformedInput: DTD prohibited by XML security policy).
- reference: none yet. [exploratory oracle triple 2026-09-26, 396x403 transparent, 3/3 byte-identical SHA-256 c9abedd317b8eb2708852ae5ed8603d8e5dc39f66ebda5d49b47c073be949211, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

## Selected documents (batch 4, 2026-09-26)

Public-domain SVGs sourced from Wikimedia Commons (all LicenseShortName=Public domain per Commons API extmetadata, 2026-09-26). Batch 4 closes the live even-odd and live opacity gaps from the batch-3 census: inv-013 carries live root fill-rule/clip-rule evenodd plus per-path opacity; inv-014 carries six stroked paths with per-path opacity (third DTD security rejection). Files live in tests/Fixtures/candidates/ with provenance; no CC0 SPDX headers; never copied into tests/Fixtures/original/.

### inv-013 — 5 linked circles with transparent background

- file: tests/Fixtures/candidates/inv-013.svg (SHA-256 12943A6DD558720D49C2A4D3BD49C523453735BB940C6D6A4F4C19555353C2A4)
- source: Wikimedia Commons, File:5 linked circles with transparent background.svg. Retrieval 2026-09-26.
- license: PD-shape (Commons).
- retrievalDate: 2026-09-26.
- archetypes: live even-odd fill and clip rules, per-path opacity, fractional stroke width (first blocker: root xml:space).
- content: 5 paths, root fill-rule/clip-rule evenodd, opacity .25.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: SVG root attribute xml:space is not implemented).
- reference: none yet. [exploratory oracle triple 2026-09-26, 1000x800 transparent, 3/3 byte-identical SHA-256 d5bf3f1f2a5232c767bb4302be338e1b8cc92d349e172f7b96181fe87ec11dfc, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

### inv-014 — Blank 6 circle Venn rotated view

- file: tests/Fixtures/candidates/inv-014.svg (SHA-256 C8A10CA8F72B99685EE46C0D79064B15A2F9D456E98CF4A2973D63A996BF1D67)
- source: Wikimedia Commons, File:Blank 6 circle or 6 set Venn diagram with limited overlap rotated view.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: per-path opacity, stroked paths, viewBox-only viewport (unrenderable: DOCTYPE present).
- content: 6 paths, defs, opacity on all paths.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: rejects (rejects:MalformedInput: DTD prohibited by XML security policy).
- reference: none yet. [exploratory oracle triple 2026-09-26, 1000x800 transparent, 3/3 byte-identical SHA-256 f73c86a3289d7cb7ab70ca5b6a4928b9a2439b9d14e6b6078fff161668e99965, staged under ignored artifacts/inventory/ (not admitted, third-party files never enter manifest.json)].

## Selected documents (batch 5, 2026-09-26)

Public-domain SVGs sourced from Wikimedia Commons (all LicenseShortName=Public domain per Commons API extmetadata, 2026-09-26). Batch 5 closes the rotation gap: inv-015 carries a live rotate(040 250,250) transform. inv-016 adds nested Inkscape group transforms. Files live in tests/Fixtures/candidates/ with provenance; no CC0 SPDX headers; never copied into tests/Fixtures/original/.

### inv-015 — BSicon numN040

- file: tests/Fixtures/candidates/inv-015.svg (SHA-256 567063C92D30993E824E1B8CF48310B0F1211FA8CA8DAA79732436E48CF617B5)
- source: Wikimedia Commons, File:BSicon numN040.svg. Retrieval 2026-09-26.
- license: PD-shape (Commons).
- retrievalDate: 2026-09-26.
- archetypes: rotation transform rotate(040 250,250), fractional path coordinates, px units.
- content: 1 path, style fill/stroke presentation.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only integer path coordinates are implemented; fractional and scientific notation stay unsupported).
- reference: none yet.

### inv-016 — Location pin icon

- file: tests/Fixtures/candidates/inv-016.svg (SHA-256 7170268EF2569E4F7D13A6ECC917AAC846EE50277E609D697BC9F92463D3EDAE)
- source: Wikimedia Commons, File:Location pin icon.svg. Retrieval 2026-09-26.
- license: PD (Commons).
- retrievalDate: 2026-09-26.
- archetypes: nested group transforms, Inkscape/sodipodi metadata, fractional viewBox.
- content: 2 paths, 2 transforms.
- variants: intended/small/large x transparent/white/black (declared, unmeasured).
- status: unsupported (rejects:UnsupportedFeature: Only integer path coordinates are implemented; fractional and scientific notation stay unsupported).
- reference: none yet.
