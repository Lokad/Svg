# Lokad.Svg — pixel-exact corporate-logo delivery plan

Updated 2026-10-06. **Current validated renderer checkpoint: native rational
round-stroke controls and signed contacts.** Straight solid open strokes retain
round caps, dots and joins through final float mapping. Reversed inner joins
preserve the original native quadrant splits. Typed nonzero stroke contacts use
signed analytic walking; ordinary fill/contact gates retain their scope.

Four original research references and three fresh reversed-join configurations
become exact. All eighteen supplemental configurations improve; fifteen remain
inexact. Their 54 captures are healthy, cleaned and stable. The bank has **1,033
fixtures = 844 exact + 162 known differences + 27 rejections**, 36 research probes
and 92 legacy Default cases: **1,161 executed cases**. All 837 prior exact fixture
PNGs and 70 exact corporate PNGs are preserved. Validation passes **3,634 tests**,
the nine-project Release build, all 25 memory scenarios and delivery checks.
Sixteen matched cost recipes record actual work, storage and local timing; Git
identifies implementation without source/build hashes.

The frozen corporate cohort remains **27 documents / 258 variants**: **70 exact,
170 known differences and eighteen rejections**. Ford, ROC HCVS, Artsy and YouTube
remain **4/27 exact documents**, marks **3/13**, wordmarks **1/14**. Every Rede
Manchete variant improves: small 1,373 → 1,267, intended 2,508 → 1,637, large
3,411 → 1,452 pixels on every background. Every disposition and exact image is
preserved. The known width-one round-cap probe worsens 15 → 16 differing pixels,
alpha-error mass 18 → 42 and maximum alpha error 3 → 11; it remains measured
against the original reference, without tolerance or a claimed explanation.

**Next: thin/fractional stroke events, closed/curved/dashed construction, clipping
and corporate residuals.** Preserve the numeric, mapping, material, bitmap-origin,
AA-clip and rational-curve repairs. Conical/multistop coordinates, notched edges,
successive clips and cache history retain separate gates. General fidelity,
representative performance, Linux and S17 remain open.

The reviewed renderer baseline
`f633c86bc5df0584edd1c4741c0a2218d49d3a32` is historical. Curve/walk separation,
rejected endpoint models, full-canvas paint routing and mask/run controls remain
preserved checkpoints; their evidence is in the records linked below.
The September 28 baseline and review remain historical evidence in
[the review](docs/REVIEW_2026_09_28.md) and Git. R01–R41 and S1–S17 retain
their identities. This plan owns the current execution queue and exit criteria;
checkpoint summaries below retain their dates, while detailed experiments stay in
[the rasterization record](docs/RASTERIZATION_DESIGN.md), document evidence in
[the inventory](docs/INVENTORY.md), and timings in
[the benchmark record](docs/BENCHMARKS.md).

**Objective:** render most ordinary, static, self-contained corporate SVG logos,
including outlined wordmarks, pixel-exact against the pinned browser oracle.
The S17 target is **at least 80% exact documents**, both overall and within each
corporate-mark/outlined-wordmark stratum of a frozen representative cohort.
Every declared variant of a counted document must be pixel-exact; the document
target never permits pixel tolerance. Preserve the pure-managed,
dependency-free library contract.

The outlined-wordmark checkpoint is established by Ford (`inv-002`): all three
sizes × transparent/white/black are exact. The independently selected flat curved
corporate-mark checkpoint is established by all nine exact YouTube variants. The
[cohort manifest](tests/References/inventory/corporate-cohort-v1.json) freezes
membership and options before fresh candidate comparison. Its 13 corporate marks
and 14 outlined wordmarks require at least 11 and 12 exact documents respectively,
so satisfying both strata requires **at least 23/27 exact documents**. The rounded
overall 80% minimum is 22/27 and remains a separate check; it cannot waive either
stratum's minimum. Retained exports have historical measurements;
only the eight new exports are fresh holdouts. This first public-domain cohort
has source/organizational-selection limits recorded in the
[inventory](docs/INVENTORY.md#frozen-corporate-cohort-2026-10-05). Neither the freeze
nor the calibration fixtures establish a release rate.

## 1. Current validated checkpoint and its limits

The implementation accompanies `SvgRoundStrokeConicTests`, sixteen compiled
native cases with 88 control/endpoint/weight rows and eighteen fresh browser
configurations. Four original round-stroke probes graduate with their original
SVG/PNG/provenance. Native cap and join controls match every recorded float bit;
this does not establish parity of the complete native stroke tape or thin-stroke
rasterization. Scope and the retained width-one regression are in
[the design record](docs/RASTERIZATION_DESIGN.md#native-rational-round-strokes-and-signed-contacts-2026-10-06).
The prior DOCTYPE [portable record](tests/References/declaration-only-doctype.json)
retains its historical measurements and implementation commit. Detailed build
diagnostics stay under ignored artifacts. Historical snapshots below retain
their dated scopes and counts.

| Validated checkpoint evidence | Assessment |
| --- | --- |
| Recorded Windows locked restore / Release solution build | Nine projects, zero warnings/errors. |
| Recorded full tests, including corporate selection/measurement | **3,634 passed**: 3,468 core, 160 visual-project, six FontProbe; zero failed/skipped. Test counts are not image counts. |
| Recorded memory and delivery checks | All 25 memory scenarios pass; runtime/symbol package inspection and isolated installed-consumer checks pass. The package retains the complete Skia/Chromium BSD notices, including the 2010 AA-dispatch, 2018 dash-interpolation and 2025 classifier notices. No runtime package dependencies, native assets or packaged SVG fixtures. |
| Executed fixture manifest | **1,033 entries = 844 exact + 162 known-difference renders + 27 rejections**; 543 base documents / 490 variants. All 837 prior exact complete PNGs are preserved. Synthetic coverage is not corporate-logo coverage. |
| Executed research manifest | **36 probes = 35 known-difference renders + one rejection**. Four original round-stroke probes graduate with unchanged SVG/PNG/provenance. Original research assets remain present. |
| Legacy independent-file Default-options sweep | **92 files = 92 render**. Six known Default PNGs change, without a matching-options fidelity claim; every outcome is preserved. The candidate directory has **100 files**; eight new holdouts belong to the corporate matrix. |
| Validated Ford all-variant evidence | **All nine exact**: 250×100, 500×200 and 1000×400 × transparent/white/black. Large variants improve 10/10/7 → 0/0/0 changed pixels; transparent alpha error 16 → 0. The six prior exact PNGs retain complete identity. All original references and current candidate identities remain portable regressions. This establishes the outlined-wordmark checkpoint, not a corporate-cohort rate. |
| Corporate cohort freeze | **27 documents / 258 variants**, independent of fresh candidate success: every one of the 19 retained brand/organization exports plus eight predeclared new holdouts. Primary strata: 13 marks / 14 wordmarks. Every original source and historical reference is preserved; materializations change only root sizing. Fifteen large cases exceeded the transport cap at freeze; all now have stable references at their original dimensions. The legacy 92-file Default bank and calibration bank are separate. |
| Recorded corporate measurement | **258 references = 240 rendered comparisons (70 exact, 170 known differences) + eighteen UnsupportedFeature cases**. Every disposition and all 70 prior exact PNGs are preserved. Nine Rede Manchete PNGs improve; the other 231 rendered PNGs are unchanged. Exact documents remain **4/27**, marks **3/13**, wordmarks **1/14**. S17 remains open. |
| Outside current Windows validation | Linux/remote CI, noncorporate inventory variant regeneration, representative throughput and private-font metrics/pixels. Chrome smoke, memory, package/consumer and artifact-validator checks pass. Sixteen matched recipes report work/storage changes; local timings do not establish representative performance. |

### Native rational round strokes and signed contacts (2026-10-06)

Native round caps/dots and joins retain weighted quadratic verbs through final
float mapping. Inner joins build the forward native arc before reversing its
verbs; rebuilding from the other endpoint moves the quadrant split. Typed native
nonzero stroke crossings, self-touches and coincident contacts use signed analytic
walking, while intersecting rings cannot select convex fill. Duplicate contour
merging preserves surviving controls and global indices. Closed/curved/dashed
construction keeps its earlier route and still needs independent native evidence.

The prior 1,143-case bank changes fourteen PNGs: one fixture, seven research and
six legacy Default. Four research cases become exact; the fixture improves
17 → eight differences. The fractional join improves three → two pixels,
while its maximum alpha error rises 15 → 20. Every outcome/input and all prior
exact PNGs are preserved. The width-one cap regression above remains explicit. All eighteen
fresh configurations improve, three to exact; their declarations preceded
capture/comparison but followed the edit. Corporate exact documents remain 4/27.

Twenty-eight core cases verify all 88 original native point rows, six outputs,
placement, duplicate-dot merging, cached clipped/unclipped setup, work/memory
denial, owner release, cancellation tokens and stroke contact interiors. Builder
columns and returned verbs are reserved through scene transfer; retained span
size stays 32 bytes. Sixteen matched recipes show both added work/storage and
reductions on selected inputs, without a general performance claim. Studies stay
under ignored `artifacts/round-stroke-conics-20261006/`.

### Retained conic fills and live convex curves (2026-10-06)

Native conics convert after final mapping at 0.25 device pixels. Expanding convex
curves to lines too early loses native advancement and smoothing: the first
prototype made two small-circle probes exact but worsened known ellipse controls.
Live rational convex walking closes six original probes and one existing fixture.
Simple ovals agree under both fill rules; polynomial even-odd and convex gates
retain their existing scope.

The prior 1,125-case bank changes 31 PNGs: ten fixtures, six research and fifteen
legacy Default. No prior exact image, outcome or changed-pixel count regresses.
One mixed circle/dash alpha-error mass rises 1,702 → 1,737. All eighteen supplemental
configurations improve but retain fractional/clip/material residuals. Original
references and corporate inputs stay unchanged. Eleven corporate PNGs improve,
without adding an exact document.

Twenty-one core cases verify 134 original native point rows, placement, six
outputs, cached native point count, clipping, work/memory denial, float collapse,
overflow and cancellation-token preservation. `SvgQuadSpan` stays 32 bytes; weights
use former padding and builder weight columns allocate only for rational verbs.
The converter adds no heap storage. Fourteen matched cost recipes and validation
stay under ignored `artifacts/conic-retention-20261006/`.

### Native open-stroke construction and winding (2026-10-06)

Native miter joins replace the endpoint; the inner chain crosses through the
centerline pivot. Splitting these overlaps into touching bands or treating an
intersecting ring as convex loses the stroke hole. The shared kernel now retains
native float points and uses a typed nonzero winding path for straight, solid open
strokes. Ordinary straight crossing guards remain. Closed, curved and dashed
strokes keep their existing construction; broad replacement regressed two exact
closed fixtures and exceeded limits on three large corporate variants, so it was
rejected on that evidence.

Eighteen exact fixture configurations are gained: three existing, six graduated
research and nine fresh controls. Round joins improve but retain the conic gate;
three fresh square-cap/bitmap-bound cases remain known differences. Nineteen prior
bank PNGs change (four fixtures, ten research, five legacy Default); nine corporate
PNGs change. Every outcome and prior exact PNG is preserved. No mismatch count or
alpha-error mass increases, but one round-join maximum rises 14 → 22.

Nineteen new core cases verify all 49 native point rows, direct/inline/inherited/
dashed bevel semantics, six-output parity, hole preservation, cached setup denial,
float collapse and cancellation. Three during-normalization cancellation studies
observe the original token. No heap owner or public API is added. All 36 fresh
browser captures are stable and healthy; the supplemental controls were declared
after the production edit. Detailed studies stay under ignored
`artifacts/stroke-construction-20261006/`.

### Bitmap-local straight edges and float root mapping (2026-10-06)

The traced Artsy paths match the authored float inputs. Native root multiply/add
and clipping relative to the owning CPU bitmap close its four remaining variants:
intended transparent/white 156 → zero and large transparent/white 10 → zero.
All nine variants now pass against the original images. Retained curve mapping
and the proper-crossing guard keep their existing gates.

The original 1,101-case bank preserves every disposition and every fixture PNG.
Six research and ten legacy Default PNGs change. Float rounding creates tiny
proper crossings in two generated stroke outlines, selecting the existing
fallback and worsening those known differences. Allowing them through the walker
was substantially worse and is rejected. The full corporate matrix also retains
small increases in known mismatch counts; no reference or tolerance changes.

Four new core cases cover six-output parity, a narrowed SVG clip, work/memory
denial and cancellation cleanup. The immutable clip context adds an origin
without a heap owner. The expanded bank has 1,113 cases. Detailed studies stay
under ignored `artifacts/corporate-edges-20261006/`; measured costs are in
[BENCHMARKS](docs/BENCHMARKS.md). Generated stroke topology, broader curve/clip
mapping, conical/multistop coordinates, performance, Linux and S17 remain open.

### Native gradient coordinates and normalized material (2026-10-06)

Linear and centered radial paints use float unit matrices and bitmap-local
pixel-center seeds. Affine concatenation/inversion retains native intermediate
precision, with a draw-local cache for the current bitmap. CSS legacy RGB
normalizes by division before fused factor/bias interpolation; gradient paint
opacity uses its float normalization. Source stop-opacity and byte-encoded CSS
alpha retain their existing contracts. Gradient-transform tokens now use the
shared float reader with bounded additional work and cancellation.

Six existing fixture configurations and the nonuniform radial research ellipse
become exact against their original browser images. All 787 previous exact PNGs
and every prior-bank disposition are preserved; no known mismatch count increases.
Nine fresh linear/radial/affine cases are exact. Three fractional objectBoundingBox
path controls improve 735 to 730 differences and retain the separate geometry gate.
All 36 fresh image captures and three numeric captures are stable, healthy and
cleaned. All nine SDL variants improve by two/one/three pixels at small/intended/
large sizes on each background. The other 231 rendered corporate PNGs are unchanged;
exact corporate documents remain 3/27.

Five new core cases cover 192 original compiled native coordinate/material vectors,
browser numeric bits, shared work/cancellation and two additional six-output
controls. Existing native vector assets are unchanged; their tests replay their
supplied byte-normalized float inputs through common dither/coverage/blend stages.
No heap storage or scene owner is added. Full validation and matched costs remain
under ignored `artifacts/gradient-coordinates-20261006/`. General conical/multistop
sampling, geometry/clip events, cache history, representative performance, Linux
and S17 remain open.

### CPU bitmap-local gradient dithering (2026-10-06)

Native dithering uses bitmap-local pixel indices even while the gradient samples
canvas-space coordinates. The owning context excludes shared borders; the final
context owns the remaining two border pixels. The grid computes that origin
without a tile tape, and every rectangle/polygon linear/radial route uses it.
Shader interpolation, geometry and pixel work-accounting remain unchanged.

The original material-pair triple graduates without recapture. Large cubic
linear/radial controls improve 19,410/18,664 → one/four changed pixels. Twelve
independently declared configurations add nine exact cases and three radial
cases with four RGB-only differences each. All 36 captures are healthy, cleaned
and stable. The prior 1,077-case bank changes only five known fixture PNGs and
SDL's unreferenced Default PNG; every disposition and all 775 exact PNGs are
preserved. The expanded bank executes 1,089 cases. Six intended/large SDL variants
improve while all 66 exact corporate PNGs remain unchanged; exact documents stay 3/27.

Three new core tests cover 3,170 original compiled native phase/material vectors
and six-output parity for large rectangle shaders. Existing context, denial and
cancellation controls pass. No storage or ownership is added; all 25 work ledgers and
the preceding 21 peak ledgers remain unchanged. Four new peak measurements vary
with encoded output size; their exact deltas are in the benchmark record.
Evidence is under ignored
`artifacts/gradient-stages-20261006/`. Native gradient coordinate construction,
remaining edge/clip events, cache history, representative performance, Linux and
S17 remain open. Changing interpolation alone was rejected: it closed no case.

### Source stop-opacity and shared polygon shaders (2026-10-06)

Stops retain source opacity alongside byte color through clamping, monotonic
offsets and href/style resolution. CSS functional color alpha keeps its existing
byte encoding: fresh browser controls disprove the previous assumption that
`stop-opacity=".5"` and `rgba(...,.5)` produce identical pixels. Gradient/radial/
pattern polygon samples now retain float lanes through ordered analytic events
and fallback coverage, using the established rectangle material stages.

Eighteen fixtures and one radial research probe become exact without replacing
their original browser images. Twenty-four fresh configurations add twenty-one
exact references and three notched-pattern known differences. Eighteen were
declared before production editing; six supplemental alpha controls were declared
after editing and before captures. All 72 captures are healthy, cleaned and stable
in triples. The prior 1,053-case bank has 69 changed inexact PNGs, no increased
mismatch counts, no disposition drift and no change to its 735 exact PNGs.
The expanded bank has 1,077 cases. All 258 corporate outcomes and 66 exact PNGs
remain unchanged; 27 variants improve without changing the 3/27 exact-document rate.

Twenty-seven new core cases cover stop parsing, six-output parity, denial and
cancellation cleanup. Native pixel tests also exercise all three polygon material
operation kinds. Stops remain 16 bytes on x64; analytic material grows from 5 to
24 bytes as a stack value. All 21 measured work/peak ledgers remain unchanged.
Full validation and matched costs are retained under ignored
`artifacts/shader-stops-20261006/`. Coordinate precision, notched edge coverage,
successive clip/cache behavior, representative performance, Linux and S17 remain open.

### Float rectangle shader arithmetic (2026-10-06)

Linear/radial rectangle paints and repeated pattern samples retain premultiplied
float channels through paint opacity, centered gradient dithering, byte coverage
and fused source-over. RGBA8 storage rounds once with native x64 nearest-even
conversion. Solid integer paint arithmetic and polygon operation routing retain
their existing gates. The 5,760 compiled native vectors match; the extracted stage
bodies are identical at the release-tag Skia pin and the studied main revision.

Nineteen fixtures and six research probes become exact against their original
browser images. Twelve independently declared configurations add nine exact
cases and three multistop-alpha known differences, with 36 healthy, cleaned,
byte-stable captures. The prior 1,041-case bank preserves every disposition and
all 701 exact PNGs; 28 previously inexact PNGs change. The expanded bank has 1,053
cases. All 258 corporate outcomes, all 240 PNGs and every comparison are unchanged.
Exact documents remain 3/27. No original reference is replaced or recaptured for
graduation, and unchanged corporate metadata is preserved.

Thirteen core tests cover compiled pixels, six-output parity, denial and cleanup.
Full tests, memory/Chrome/validator checks and delivery checks accompany this
slice. Sixteen matched ledger/timing recipes retain its actual cost under ignored
`artifacts/shader-paint-20261006/`; see the benchmark record. Authored stop-alpha
precision, coordinate/interpolation stages, polygon materials, successive clips,
cache history, representative performance, Linux and S17 remain open.

### Retained rectangle paints and native coverage (2026-10-06)

Actual rectangles now retain their original paint edges and a separate clip.
Viewport, affine, symbol and use mapping move both together; gradient/radial/
pattern fills and generated solid stroke bands preserve the clip. Pattern tile
content applies it before repetition. Native plain rectangles use rounded 24.8
edges, with the one-row/one-column minus-one specializations; solid material uses
initial round/255 premultiplication before coverage. The full-canvas float route
remains eligible only when its retained clip contains the whole output.

The 960 compiled-native pixel vectors and fifteen new core tests cover precision,
clip ownership/mapping, denial, cancellation and all six output forms. Twelve CC0
sources add 36 configurations backed by 108 healthy, cleaned, byte-stable fresh
captures: thirty exact and six known shader differences. The first thirty cases
were declared before production editing; six supplemental cases were declared
after the edit and before captures. The original material grid and translucent
border band graduate to exact without replacing their browser images.

The 1,005-case prior bank preserves every disposition and all 669 prior exact PNGs.
Only the two graduated controls and the unreferenced legacy `inv-057` PNG change.
The expanded bank executes 1,041 cases. All 258 corporate dispositions and all
66 exact PNGs are preserved; nine IBM variants improve but remain inexact.
Exact documents remain 3/27. The retained clip grows rectangle/geometry values
by 48 bytes on Windows x64; existing array reservations account for that cost.
Mapped-edge setup is charged before painting. Full tests, memory/delivery checks
and matched local observations pass; evidence is under
`artifacts/rect-paint-20261006/`. Shader quantization, successive clip-alpha
intersections, cache history, general fidelity and S17 remain open.

### Native dash endpoints and orthogonal contacts (2026-10-06)

ROC's missing one-byte column comes from native float dash interpolation plus
the raster route for coincident stroke outlines. Float cuts alone leave the
fan fallback unchanged. The native walker preserves the fringe after orthogonal
sets pass its signed-winding contact gate. Classification is cached, charged once
and cancellable. Retained curve spans, self-touching and coincident even-odd
sets keep their separate gates. All nine original ROC variants are now exact.

Four CC0 inputs add twelve exact configurations: horizontal/vertical compound
paths, a single-line control and translated stroke opacity, on all backgrounds.
All 36 captures are healthy, cleaned and stable in triples. The separate Greek
flag has another three healthy stable captures and is exact at Default 600×400.
Ten core tests cover 96 compiled native float vectors, native one-byte coverage,
classification caching, denial cleanup, cancellation and all six output forms.
The 993-case retained bank preserves every disposition and all 657 exact PNGs;
only ROC and the Greek flag's Default PNGs change. The expanded bank has 1,005
cases. Corporate exact variants advance 60 → 66; exact documents advance 2 → 3.
Matching guard ledgers are unchanged; affected ROC/Greek raster work drops while
accounted peaks grow by 11,290 / 1,039 bytes. Complete local evidence stays under
`artifacts/dash-endpoints-20261006/`. This does not establish general stroke,
rectangle paint, affine, clip-stack or S17 completion.

### Orthogonal fill clip preservation (2026-10-06)

The eleven former lowered-rectangle differences were integer orthogonal paths
whose antialiased viewport clip was folded into rectangle bands. They now use
the existing polygon paint route with their original contour and separate clip;
`polygon` and filled `polyline` retain the same ownership. All twelve original
controls are exact, including the previously exact black-background case.
Original browser images and capture provenance remain unchanged.

Six CC0 box/notch inputs add eighteen scaled configurations with 54 healthy,
cleaned, byte-stable fresh captures. All eighteen comparisons are exact. The
975-case retained bank changes only the eleven now-exact controls; all 628 prior
exact PNG identities and every disposition are preserved. The expanded bank has
993 cases. All 258 corporate outcomes and all 240 PNGs/comparisons are unchanged.
Parser memory/work denial, cancellation after raster preparation, six-output
parity, full tests and delivery checks pass. Matching six-input work/peak ledgers
are unchanged. Complete local evidence is under
`artifacts/rectangle-route-20261006/`. Actual `rect` paints and paint-server bands
are separate from this bounded repair; general clip stacks remain open.

### Bounded corporate capture checkpoint (2026-10-06)

The oracle tool now admits 8,388,608 pixels with a 16,384-pixel side bound.
Only screenshot replies receive a 64 MiB allowance; commands, other replies
and retained events keep 16 MiB. Fragmented receive, cancellation, event bounds,
startup diagnostics, profile cleanup and atomic PNG publication remain checked.
The capture recipe and browser pin are unchanged. An opt-in
`tools/capture-corporate-cohort.ps1` uses the immutable manifest, verifies input
and pin identity, and records fresh triples and failures under ignored artifacts.

All fifteen former gaps have 45 stable healthy captures. Eighteen additional
Ford/YouTube overlap captures are byte-identical to their six original references.
All 258 renderer dispositions and 240 candidate PNGs are unchanged. Newly
available comparisons show one exact Artsy black variant and eleven differences;
three Target variants retain UnsupportedFeature. Exact documents remain 2/27.
A separate maximum-area noise capture returns a 32,270,774-byte PNG (more than
41 MiB as base64), proving the enlarged screenshot-reply path is exercised.
Locked restore, the nine-project Release build, all **3,472 tests** (3,314 core,
152 visual, six FontProbe), and the three-process Chrome smoke pass. Wrapper
failure/timeout controls preserve the destination and leave no partial files or
profiles. The renderer's resource/package evidence remains the preceding
clip-byte checkpoint. Full evidence is under
`artifacts/cohort-transport-20261006/`.

### Rectangle clip-byte checkpoint (2026-10-06)

The pinned native rectangle walker emits left/right masks **217/216, 179/178,
103/102** at the three YouTube sizes. Managed floating-area multiplication had
discarded these byte distinctions. The small (0,74) witness combines path alpha
50 with clip byte 217 to produce 43, rather than the former rounded area result
42. All 65,536 path/clip byte pairs match compiled `SkMulDiv255Round`.
Coverage preserves X truncation to 1/256, quarter-Y snapping, partial-row run
snapping and the initially-BW nearly-integral shortcut. Allocation-only bounds
retain their geometric fallback. No coverage buffers or geometry change.

Eight CC0 sources / twenty-four configurations have 72 healthy, cleaned,
byte-stable fresh captures. Twelve nonrectangular path variants are exact;
separate rectangle fast-path controls retain eleven known differences and one
exact black-background configuration. Their baseline PNGs are unchanged. The
rectangle controls were declared before production editing; supplemental path
controls were declared after that edit and before their captures.

The 951-case prior bank preserves all 614 exact complete PNGs and dispositions.
The known clipped-phase fixture improves two pixels to zero and graduates; one
legacy Default YouTube output changes without its own reference. The expanded
bank executes **975 = 823 fixtures + 60 research + 92 legacy Default cases**.
Corporate dispositions are unchanged. Nine YouTube variants become exact, all
nine Ford PNGs remain identical, and two unmeasured small `inv-005` PNGs change.
At this checkpoint the verified cohort numerator was **2/27**: marks **1/13**, wordmarks
**1/14**. This does not satisfy the 23-document stratum target.

Matching six-input ledgers preserve all parser/raster work and four peaks;
YouTube peaks grow by 16/4 bytes with changed encoded outputs. All local gates
are under ignored `artifacts/boundary-clip-20261006/`. Rectangle paint routes,
successive clip-alpha intersections and native cache history remain independent
work; the completed two selected checkpoints do not complete S11/S14 or S17.

### Retained convex rectangular-strip checkpoint (2026-10-06)

The pinned native convex walker emits **71 + 25 = 96** at YouTube (457,258),
where the prior managed walker emitted 71 + 24 = 95. Native mapping plus clipped
edge construction and supplied-edge replay agree. Ordinary direct white paint
over opaque gray 128 then produces the reference's gray 176. The raw-box decision
is cached before clipping and passed to the walker; no buffers or geometry change.
This fixes ordinary convex strips while preserving existing rectangle calibration.

Six supplemental CC0 sources / fourteen configurations have **42 healthy, cleaned,
byte-stable fresh-process captures**: thirteen become exact; the shader improves
76 → 66 changed pixels. These numeric controls were declared after the initial
prototype/production edit and before their captures. Earlier unsupported
percentage-coordinate attempts remain under ignored artifacts.
The 937-case prior-bank comparison preserves all 601 exact PNGs and dispositions;
one known fixture improves 11 → nine pixels / 34 → 32 alpha error, and four
Default outputs change without matching references. The expanded bank executes
**951 = 799 fixtures + 60 research + 92 legacy Default cases**.

All 258 corporate dispositions and all nine Ford PNGs are unchanged. Twelve PNGs
change: six YouTube variants and six unmeasured Arkema holdout variants. YouTube
transparent changed pixels improve 177 → 37 and 312 → 26; alpha error stays
37/26. Small-size output is unchanged. The remaining differences are boundary
clip witnesses at the first/last pixel columns in every variant; the curved-mark
checkpoint is still open. Matching six-input
ledgers preserve charged work and four peaks; YouTube peaks grow by 18/40 bytes
with the changed encoded outputs. Full local diagnostics are under
`artifacts/triangle-paint-20261005/`; current tests, fixture references and the
corporate measurement are the portable regressions.

### Retained declaration-only DOCTYPE checkpoint (2026-10-05)

The shared prolog guard rejects internal subsets and duplicates before the main
reader skips an admitted declaration. It retains original byte input and BCL XML
encoding interpretation, checks cancellation/work in bounded chunks, and reserves
an additional fixed 32 KiB workspace only when a prolog is present. That workspace
is released before main parsing; existing conservative reader allowances remain.
Root parsing and discovery borrow their caller's work counter; output-scale
probing retains its existing separately bounded probe counter. There is no DOM,
whole-input string, external resolution, DTD defaulting or declared-entity expansion.

The [portable record](tests/References/declaration-only-doctype.json) retains the
predeclared 18 semantic cases across nine encodings, resource controls, all four
legacy disposition changes and all 36 corporate advances. All **601 exact fixture
PNGs**, every previous known-difference PNG and every original source/reference
are unchanged. Three corporate documents become renderable; Target remains
unsupported at a subsequent style gate. No newly admitted corporate variant has
an exactness claim. Full declaration-checkpoint validation is recorded separately
from the retained AA proof below. Matching six-input ledgers preserve every PNG;
prolog-bearing inputs add one or two parser work units. The tiny case peak grows
132,608 → 163,840 bytes; the other five measured peaks are unchanged. Timings retain
both process orders and do not establish representative throughput.

### Retained validated AA-clip checkpoint (2026-10-05)

The shared dispatch separates the paint clip from construction clipping
and cached geometry. AA paint contexts force additive run storage and forward
fringe pairs through the ordinary paint route across all four materials.
Per-boundary provenance distinguishes geometric clip edges from mask allocation
bounds and survives intersection, reflection and reference placement. This
preserves the original full-white-mask equality regression; the superseded draft
changed that output incorrectly.

The [portable record](tests/References/antialiased-paint-clip-dispatch.json)
identifies implementation commit `86554a94`. Its validation state is a
validated precommit checkpoint. The corporate observations in this subsection
describe that AA renderer. The current corporate measurement record describes
the convex rectangle-strip renderer; earlier measurements retain their historical scope.

| Evidence at this validated renderer snapshot | Assessment |
| --- | --- |
| Independent controls and portable banking | **15 CC0 sources / 41 configurations / 123 successful captures**, healthy, cleaned and stable in triples. Twenty-seven configurations were declared before production edits; fourteen supplemental configurations were declared after implementation and before their captures. Results: **21 exact + eleven known differences + nine UnsupportedFeature rejections**. Original references and failed capture-attempt diagnostics are retained. |
| Native classification and affected contracts | **188 compiled-native predicate/edge-setup vectors** match the pinned source. Managed tests cover context-local storage/cache behavior, boundary provenance, tight work/memory denial, cancellation cleanup and four paints × six outputs. Eight independent material-grid fringe witnesses match; full-white-mask and unmasked browser twins preserve the original reference. This does not execute complete native AA-mask construction, successive clip-alpha intersections or all cache transitions. |
| Prior-bank regression comparison | All **896 prior cases** retain their dispositions and all **580 exact fixture PNGs** retain complete identity. Two PNGs change: the known clipped-phase fixture improves **164 → two pixels / 298 → two alpha error**; the legacy Default YouTube output has no matching reference in that sweep. The mask regression is unchanged after the provenance repair. |
| Expanded bank | **937 executed = 785 fixtures + 60 research + 92 legacy Default cases**. Fixtures: **601 exact + 157 known differences + 27 rejections**, across 443 base documents / 342 variants. All 601 exact entries pass their original references. Research and legacy dispositions remain unchanged. These are calibration/regression counts, not corporate-document coverage. |
| Frozen corporate regression comparison | All **258 dispositions** and all nine exact Ford PNG identities are preserved. Fifteen corporate PNGs change: nine measured YouTube variants and six `inv-005` variants without references. The latter six have no measured fidelity conclusion. The eighteen available comparisons still verify **1/27 exact documents**; **225 unmeasured + fifteen transport gaps** remain. |
| Completed current-snapshot gates | **3,257 tests pass**: 3,109 core, 142 visual and six FontProbe, zero failed/skipped; all **25 memory scenarios pass**. Normal Release build, runtime/symbol package, installed consumer, image probes and timing/resource runs identify the same final renderer. Final-source dispatch suppression restores every old PNG/disposition across 41 controls and 258 corporate cases; unchanged renderer sources bind that proof across the assembly-metadata refresh. |
| Measured resources and remaining release gates | Matching six-input timings/ledgers retain both process orders and the storage cost of clip metadata. Large YouTube is slower in both final ordering pairs; intended YouTube and other timing directions vary with order/host load. Native AA masks/cache histories, broader fidelity, complete cohort references/transport, representative throughput and Linux remain open. |

YouTube transparent differences are **30/177/312 pixels**, with alpha error
**30/37/26**, maximum alpha error one and no opaque/hole flips. All nine variants
remain inexact. The earlier isolated fractional-border model retained 56
small-size changed pixels; its dated results remain separate. Neither a one-byte
alpha maximum nor the twenty-one exact calibration configurations closes the
curved-mark checkpoint. Local evidence is under
`artifacts/aa-clip-dispatch-20261005/`.

### Historical validated raw-control convexity checkpoint

The pinned classifier examines raw controls, including their float direction
signs, turns, backwards reversals and implicit closure. YouTube's first control
of the second cubic reverses the expected turn; its sampled ring hides that concavity.
The shared preparation now reconstructs raw controls only for a single convex
sampled ring with admitted retained spans. It caches the compatible decision,
reserving a conservative traversal work bound before classification. No buffers
are added; genuine convex and declined-retention routes are preserved.
This does not establish native transformed-cache or general affine parity.

All 75 compiled-native vectors match. Eight CC0 sources / 24 configurations were
declared before the prototype; their 72 fresh captures are healthy, cleaned and
stable in triples. Twelve are now exact; twelve retain their original failures.
The existing fractional shoulder graduates against its original reference.
All 567 prior exact fixture PNGs and all nine Ford PNGs preserve complete identity.
The 872-case prior bank changes seven PNGs: that shoulder and six Default outputs
without matching references in that sweep. The expanded bank executes
**896 = 744 fixtures + 60 research + 92 legacy Default cases**.

All 258 corporate dispositions are preserved. Thirty-nine PNGs change: nine
measured YouTube variants improve, while thirty changed cases have no matching
reference. YouTube transparent alpha error falls 7,026/14,622/28,474 →
356/787/1,634 without opaque/hole flips. It remains inexact at every size.
Restoring sampled-ring dispatch in the isolated copy restores every old control
and corporate PNG/disposition. Fresh delivery/resource gates pass; matching local
timings and six resource ledgers are in [BENCHMARKS](docs/BENCHMARKS.md).
Full evidence is under ignored `artifacts/raw-convexity-20261005/`.

### Historical path-point density checkpoint

The two recorded Ford paths contain **254/182 native points**, while their
flattenings contain **480/421 samples**. Global and inferred tile-local mapping
models produce identical additions at all ten lower-row residuals. With identical
supplied geometry and prepared edges, changing only intersection dispatch gives
the original alpha at every witness. The earlier row-269 comparison remains
unchanged under both modes; it was a separate mapping fault.

The renderer now caches the raw count represented by admitted retained verbs
before clipping or subdivision, validating ordered contour ranges in a charged,
cancellable, allocation-free pass. Declined retention preserves flattened counts.
Clipped and uncut dispatches use that count with their actual walk height; geometry,
limits, original inputs and references are unchanged. The
[native density record](tests/References/inventory/inv-002-native-density.json)
retains the ten discriminators and adapter limitations. Restoring flattened-count
dispatch fails the native witness and all three large Ford variant tests.

Eight CC0 sources / 24 configurations were declared before the prototype. Their
72 fresh-process captures are healthy, cleaned and byte-stable in triples. The
bank retains **15 exact + three known differences + six UnsupportedFeature
rejections**. The 848-case before/after comparison preserves every prior exact
PNG and disposition; five candidates change: one known fixture, still 164
pixels / 298 alpha error, and four Default inventory outputs without matching
oracles in that sweep. Ford has separate all-nine reference evidence; the Default
sweep is not a new fidelity rate. The expanded bank executes **872 = 720 fixtures
+ 60 research + 92 Default cases**.

Fresh Windows restore/build/tests, all 25 memory scenarios, runtime/symbol package
inspection and installed-consumer checks pass. Matching resource ledgers preserve
accounted peaks and add 292 charged units for intended Ford and 51 each for dense/
native-size inputs. Local matching timings and their limits are in
[BENCHMARKS](docs/BENCHMARKS.md); ignored full evidence is under
`artifacts/lower-residuals-20261005/`. General affine/instance, fractional-event,
quad/convex/stroke and layer fidelity, representative performance, Linux and S17
remain open.

### Historical checkpoint evidence

The original review and full-canvas checkpoint retain their dated evidence under
ignored `artifacts/review-2026-10-04/` and `artifacts/span-dispatch-20261004/`.
The latter banks 79 exact routing configurations, one group-opacity-white failure
and the graduated integer overlap. The subsequent dispatch investigation banks
12 independent configurations; all 12 are now exact without replacing references.

The paint checkpoint adds 16 CC0 sources / 24 configurations, each with three
stable, healthy fresh-process captures: 15 exact and nine known differences.
Eleven existing known fixtures graduate to exact. The exact research joint-split
control graduates to fixtures with its original PNG and provenance; its former
research files remain historical. All 435 previously exact complete PNGs and all
existing render/reject dispositions are preserved. The pre-bank comparison covers
689 cases; the updated bank executes **713 = 549 fixtures + 72 research + 92
Default inventory cases**. Comparisons, capture triples, resource ledgers and
delivery checks are under ignored `artifacts/paint-operations-20261004/`.
Committed manifests and the standard tools remain the portable regression sources.

The scan-window checkpoint adds 16 CC0 sources / 18 configurations, all exact,
each with three stable, healthy fresh-process captures. Five research controls
graduate with their original PNG/provenance. All 462 previously exact complete
PNGs and every render/reject disposition are preserved. The pre-bank comparison
covers 713 cases; the updated bank executes **731 = 572 fixtures + 67 research +
92 Default inventory cases**. Evidence is under ignored
`artifacts/scan-window-20261004/`. Full independent inventory variants and corporate
coverage remain unmeasured; four changed Default inventory outputs lack matching
oracle evidence.

The one-chord cubic checkpoint adds ten CC0 sources / 12 configurations with
36 stable, healthy fresh-process captures: eight exact and four known differences.
Nine configurations were declared before implementation; the three paired-material
holdouts were declared after the first trial and before their independent captures.
All 572 existing fixture PNGs, all 67 research results and every disposition are
preserved. The updated bank executes **743 = 584 fixtures + 67 research + 92
Default inventory cases**. Eleven Default inventory PNGs change without matching
captures in that sweep; Ford has separate all-nine evidence, while the other ten
outputs' fidelity is unmeasured. Evidence is under ignored
`artifacts/cubic-admission-20261005/`; this is shared renderer progress, with no new
independent corporate checkpoint or S17 rate.

The clipping-kernel checkpoint verifies 160 independent native inputs
and all 334 emitted pieces, including raw float bits, order and verb type. It
banks 19 CC0 SVG sources / 27 declared configurations with 81 stable, healthy
fresh-process browser captures. All 27 rendered with known differences at that
checkpoint; references retained those failures before integration. All 743 prior results and
all nine Ford PNGs are unchanged; the updated bank executes **770 = 611 fixtures
+ 67 research + 92 Default inventory cases**. Kernel work/iteration limits and
cancellation were tested before the renderer called it. Ignored evidence
is under `artifacts/analytic-clip-20261005/`; the native piece data, SVG controls
and references are portable regressions. Shared preparation, edge storage and
walk-window integration required the resource and fidelity proof recorded next.

The clipped-preparation checkpoint preserves every prior exact PNG and every
render/reject disposition. The 770-case comparison changes 52 complete PNGs:
38 known fixtures, three research probes and eleven Default inventory outputs.
One existing scaled control becomes exact. Both subsequently declared stroke
construction witnesses retain their six healthy stable captures: the fractional
band is exact, while the star improves from 206 to 173 changed pixels and remains
a known difference. The updated bank executes **772 = 613 fixtures + 67 research
+ 92 Default inventory cases**, with Ford's nine variants checked separately.
All original references are retained. Ignored measurements and full comparisons
are under `artifacts/clipped-preparation-20261005/`; committed manifests and the
Ford variant bank are the portable evidence. These calibration results do not
establish an all-nine wordmark exit or an S17 rate.

Chrome remains **153.0.8010.52 win64, recipe 3**, with the recorded executable
hash and Chromium revision field 1681091. Historical recipe-1/recipe-2
references retain their identities. The matched release-tag Skia pin remains
**f8b66b7597c4cc859d3ed190e9c6872241e6721c**; the main-revision comparison
`8b8c3872` does not supersede it (R40). Baseline/checkpoint comparisons reused
committed references. The fresh recipe-3 calibration controls in section 3 do not
constitute a full oracle refresh.

The tests deliberately retain known wrong pixels as well as successes. PNG
output, admitted syntax, same-renderer twins and matching mismatch tuples are
not independent proof of exactness. The API can return inexact output; keep
the preview's support/fidelity boundaries explicit. Oracle over-coverage,
asymmetry and backend dispatch are compatibility requirements wherever they
affect a counted document, even when our geometry is closer to ideal area.

## 2. Execution decisions and ownership

1. **Use one implementation owner for the renderer queue.** Work serially across
   `SvgConverter`, geometry lowering, reference resolution and shared style/
   mapping types. A second review may be read-only; delegation is not required.
2. **Check actual state before edits.** Inspect HEAD, status and live
   coordination. Previous committed work is not a lock. Preserve concurrent
   changes and use an isolated worktree when an experiment overlaps them.
3. **Preserve direct paint and accumulated-run operations, rows 3–5.** Curve/walk state separation
   preserves quarter-grid output. The full-canvas solid-paint repair now preserves
   native rectangle identity and original fill-opacity through scene/reference
   lowering, with independent opacity, paint, mapping and routing controls.
   Independent width/storage controls now distinguish native mask and run routes.
   The shared operation/material boundary now preserves direct paints and pending
   additive runs, with distinct uniform/shader arithmetic. Paint improves every
   Ford variant. The contained-edge scan-window repair closes the retained curve
   controls while leaving Ford unchanged. Retaining one-chord cubics improves
   six Ford variants; shared clipped preparation now makes those six exact and
   improves the three large variants. Bounded CPU contexts improve those three again while preserving the six exact PNGs. Retained-path-point density now closes all three large variants; general event fidelity stays active.
   General composition remains open; its layer residuals stay in rows 3 and 7.
4. **Preserve the landed repairs and exact controls.** Do not repeat the old
   six-site floor256 replacement, globally disable live cubics, snap the final
   coverage mask globally, or desnap endpoints alone. Native run-flush snapping
   applies only to pending additive runs; direct fringe paints bypass that flush.
   Each has retained counterexamples. Further syntax admissions wait unless
   the active target or the frozen cohort's acceptance threshold demonstrates a
   dependency. The measured declaration-only DOCTYPE blocker is such a dependency,
   now admitted after the validated AA-clip checkpoint; regression repairs may interrupt.
5. **Keep release policy separate from implementation.** S17's exact-document
   target is 80%, overall and in each corporate stratum. The
   first cohort and its explicit variants are frozen; all 258 references are
   available, covering 240 rendered comparisons and eighteen retained rejections.
   Ford, ROC, Artsy and YouTube are exact in every variant: 4/27 documents. The measured
   result falls short of the target. There is no
   pixel-tolerance or oracle-quirk budget. The aligned policy record is
   [S17_DECISIONS.md](docs/S17_DECISIONS.md).

Make small, buildable commits with relevant regression evidence, support
documentation and actual validation. Update the current row/status in place.
Git retains previous checkpoints; the design record retains experiment details.
After two experiments without a mechanism-level improvement, record the
remaining uncertainty and choose the next architectural step. Do not substitute
another census, source audit or mismatch-maxima refresh for implementation.
Record a concrete blocker and return condition before switching slices.

## 3. Ordered implementation queue

Rows retain their identities and dependencies. A landed mechanism is not a
completed stage: the remaining exit evidence below governs completion.

| Order | Current state and work boundary | Remaining exit evidence |
| --- | --- | --- |
| 1 | **Complete: bounded semantic repairs.** S12 miter threshold and R41 zero-area objectBoundingBox gradient path strokes are repaired. | Preserve acute-limit branches, degenerate path/use and userSpace controls; no reopened defect. |
| 2 | **Shared contour/paint ownership boundary landed.** R30/R38/R39 preparation, mapping and reservation repairs support the scanner. | Preserve reserve-before-allocation, growth peaks and ownership transfer; audit new traversal/state storage, denial, cancellation, disposal and six-output parity. Existing foundations are not a new rewrite. |
| 3 | **Full-canvas solid-paint routing landed; general composition fidelity remains open.** Opaque coverage correction, premultiplied source-over and source-opacity precision are retained. | Preserve the 79 exact new routing configurations and the graduated integer overlap. General coverage/alpha and layer composition need independent controls; group-opacity on white still differs. |
| 4 | **Shared scan conversion landed; general event fidelity remains open with rows 3 and 5.** Spatial membership, analytic rows, contact/straight-source union, operation-preserving mask/run paint, contained-edge scan windows, retained cubics, clipped construction, CPU contexts and retained-path-point density have landed across the shared paint boundary. | Preserve all nine exact Ford variants, density/admission/cache regressions, X/Y splitting, projected winding, two-pass storage, whole-path dispatch, bitmap-bounded rows and exclusive interiors. Order remaining fractional events, crossings, counters and overlaps from the frozen corporate cohort. |
| 5 | **Retained curves and final device mapping remain partial with row 4.** Device-map threading, retained controls, live non-convex cubics, curve/walk separation, one-chord cubic admission, axis-aligned float mapping, coupled numeric reading and compatible raw-control convexity, retained conic fills, live rational convex advancement and native round-stroke conics have landed. All 436 recorded authored points and the actual Ford CTM match; density integration makes all nine PNGs exact. | Preserve transform/instance and large-translation evidence and the closed row-6, (740,30), (375,269), (269,187) and lower-row witnesses. One exact wordmark does not establish general mapping/event parity. Wider affine/instance, fractional-event and quad/convex/stroke work retains separate gates. |
| 6 | **Partial: curve-aware stroke completion.** Width/cascade, cap/join/dash, zero-width, native float line-dash endpoints, nonzero orthogonal-contact walking and native straight open-stroke winding are retained. | True curve tangents/dash cuts, curved/dashed overlap and seam handling, remaining round/miter/hairline/pathLength/non-scaling cases through the common kernel. Mathematical outline agreement alone does not close Chrome pixels. |
| 7 | **Partial: practical exports, paints and bounded layers.** Many inventory blockers, affine paint maps, use/reference bindings, retained paint clips, shared float shaders and bounded composition paths have landed. | Remaining clip/mask/style/paint combinations, alpha/dither/group composition and supported transform gates, with source-order binding, temporary-surface bounds and per-input oracle evidence. |
| 8 | **Both selected logo checkpoints achieved; S8 measurement complete for the first frozen cohort; S17 remains open.** Ford and YouTube are each exact in all nine declared variants. All 27 documents / 258 variants, including eight fresh holdouts and eighteen rejections, have browser references and reproducible reports. | Improve the measured 4/27 exact documents to the 80% target overall and per corporate stratum, with fresh platform/package/resource/performance evidence. |

### Active work order: thin/closed/curved strokes and cohort residuals (2026-10-06)

The Ford wordmark and YouTube curved-mark exits are closed: all eighteen original variants have zero differences
and the shared regression/resource/delivery gates pass. Preserve those full-image
results and the independently recorded numeric, mapping, clipping, operation and
density witnesses. This bounded repair does not complete rows 3–7.

**Selection/freeze is complete.** YouTube `inv-027` is the flat curved mark:
286×200, 572×400 and 1144×800 on all three backgrounds. The
[fixed manifest](tests/References/inventory/corporate-cohort-v1.json) retains all
19 existing brand/organization exports and eight new holdouts;
[selection](tests/References/inventory/corporate-cohort-selection.json) and
[upstream evidence](tests/References/inventory/corporate-cohort-sources.json)
retain identities, licensing, roles and the policy. Tests protect membership,
all declared variants and unchanged geometry outside root sizing.

The shared AA-clip checkpoint is validated: 188 native predicate/setup vectors,
41 independent browser configurations, full tests/resources/delivery and matched
local timing/ledger evidence are retained in section 1 and the
[design record](docs/RASTERIZATION_DESIGN.md#antialiased-rectangle-paint-clip-checkpoint-2026-10-05).
The original (179,14) caller witness and subsequent triangle/boundary residuals
are resolved; all nine YouTube references are exact. Preserve geometric boundary provenance, cached geometry,
all exact controls and Ford. Full native AA-mask/cache history remains unproved.

1. **Preserve the established opaque triangle coverage/paint repair.** The first large
   YouTube triangle residual at **(457,258), gray 176 versus 175**, is resolved
   through shared convex rectangular-strip rounding. Native supplied-edge and
   original-path construction agree on coverage 96. Preserve the raw-box guard,
   all exact fractional rectangles, the original browser references and the
   fresh phase/scale/opacity/shader controls.
2. **Preserve clip-byte coverage and orthogonal contours; investigate remaining paint routes.**
   Native X/Y snapping, clip-mask bytes and round/255 multiplication explain all
   remaining YouTube boundary witnesses. Keep the 65,536 native byte-pair controls,
   fresh horizontal/vertical/corner/opacity/BW path controls and both all-variant
   corporate checkpoints. The eleven path-band differences are now resolved by
   retaining original contours and separate AA clips; keep all eighteen new
   scaled spelling controls exact. Actual rectangles now retain original edges and
   separate clips across solid and paint-server bands. Keep the 960 native pixel
   vectors, thirty new exact controls and two graduations. The subsequent rectangle
    and polygon float repairs close those six shader differences. Preserve source
    stop-opacity, byte color alpha, 5,760 native shader vectors and the twenty-one
    fresh exact shader/stop configurations; investigate notched-edge and corporate
    residuals from native/browser evidence. Preserve clipping through pattern content, symbols and use mapping.
   Preserve native float dash cuts, the orthogonal signed-winding gate, all nine
   ROC references and the twelve fresh horizontal/vertical/opacity controls.
   Preserve original references and report improvements and regressions separately.
   Successive clip intersections, cache history, general
   affine, quad/convex/stroke and layer work retain their own gates.
3. **Complete thin/fractional events and closed/curved/dashed stroke construction.**
   Preserve native float normalization, endpoint replacement, rational caps/joins,
   forward inner-arc splits and signed contacts. Keep both the 49 earlier native
   points and 88 new control/endpoint/weight rows, all graduations and fresh
   controls. Investigate the width-one cap's measured 15 → 16 pixel regression
   and remaining fractional round/clip events against original references.
   Square caps crossing bitmap bounds retain their separate clip-event gate.
   Closed/curved/dashed replacement needs its own native/browser evidence after
   the broad model's closed-fixture regressions and corporate resource denials.

**Preserve the landed declaration-only DOCTYPE policy.** Bare, SYSTEM and PUBLIC
declarations are inert; internal subsets (even empty), duplicates and malformed
syntax remain `MalformedInput`. Root parsing, font discovery and output-scale
probing preserve input bytes/encodings and shared resource contracts. The 187 new
core controls cover nine encodings, six outputs, structural limits, work/memory
denial and during-prolog cancellation. Four legacy Default cases now render.
Zeppelin and SG Rotation Leipzig have nine known differences each; Artsy now
has all nine variants exact after the bitmap-local straight-edge repair. Coca-Cola's nine touching-contour rejections and
Target's nine `marker` style rejections remain measured blockers for subsequent
shared work when the active target or cohort acceptance threshold requires it.
Holdouts receive no document-specific tuning. All failures/gaps keep their
denominators.

**Complete first-cohort evidence.** All 258 variants now have browser references:
70 exact, 170 known differences and eighteen rejections. The remaining 225
references have 675 healthy, cleaned, byte-stable fresh captures. The original
33 references and all 258 outcomes are preserved. The straight-edge repair made
Artsy exact; native open strokes now improve all nine Rede Manchete variants. All
70 prior exact corporate PNGs and every original reference remain unchanged.
`capture-corporate-cohort.ps1` and VisualDiff's `--cohort` mode reproduce capture
and categorized per-variant/document reporting under ignored artifacts. Neither
command refreshes the immutable selection or rewrites references automatically.

Use the retained development cases to discriminate shared arithmetic: ROC has
an established exact dashed-stroke result; Red Cross differs only at large size; NASA only at
small size; Blur retains sparse differences; Artsy is exact in all variants. Their complete matrix is
in [the inventory](docs/INVENTORY.md#complete-corporate-reference-matrix-2026-10-06).
These observations order investigation; they do not prove a common cause.
Keep the now-exact original path-band controls and eighteen scaled controls;
use mechanism-level browser/native evidence before changing shared paint code.
Holdouts remain evaluation
inputs. Representative performance, Linux delivery and S17 remain open.

A bounded shared repair may land with independent mechanism-level improvement
and nonregression across all nine Ford variants. Preserve all **830 currently
exact fixture entries**, including all 812 preceding complete PNG identities,
and required categorized rejections. Intentional syntax graduations need explicit
contract/tests/provenance updates. Compare the complete **1,125-case current
bank** and all 258 frozen corporate cases; retain the AA repair's separate
896-case prior-bank comparison. Report fixture/research/legacy and corporate
regression scopes separately.
Run affected ownership/work/cancellation,
six-output and delivery checks plus matching measurements. Missing oracle results
must stay explicit. General tile-local/affine mapping, fractional events,
quad/convex/stroke and layer fidelity retain independent gates.

The current native open-stroke checkpoint passes locked restore, the nine-project
Release build with zero warnings/errors, **3,585 tests = 3,419 core + 160 visual +
six FontProbe**, all 25 memory scenarios and runtime/symbol package plus installed-
consumer checks. The expanded bank executes 1,125 cases; all prior exact images and
dispositions are preserved. Full local gates and forty matched cost recipes are
under `artifacts/stroke-construction-20261006/`. Earlier records retain their
historical validation snapshots. Future renderer edits need their affected checks.

S17 still requires at least 80% exact documents overall and in each corporate
stratum, with every counted document's declared variants exact. Neither these
two selected logo checkpoints nor fixture-entry counts establish that target.

### Landed renderer checkpoints and retained evidence

**Implemented mechanism.** Small native path bounds select an accumulated mask;
larger bounds preserve ordered direct single/pair/full-span paints separately from
pending additive coverage. Only pending runs snap at flush. Solid paints use
round/255 initial premultiplication and operation-specific coverage arithmetic,
including the opaque-black pair specialization. Linear/radial/pattern samples
retain their shader route through the same shared boundary. Capture output uses
the pinned float unpremultiplication/store order. This does not migrate the
separately gated quad/convex/stroke geometry or close general layer composition.

Full spans retain one shared descriptor with pixel indices; fringe events retain
their order relative to that descriptor. The initial per-pixel full-span model
was rejected because it lost native-size `inv-050` Default work admission.
The compact model restores admission without raising limits or changing pixels.
Reservations precede allocation, cover old/new growth peaks and release on denial
or cancellation. Recording, indexing, overlap replay, copying and traversal are
charged; cancellation preserves its token during growth and replay. Ten focused
tests cover material arithmetic, compact spans, denial, growth peaks and cleanup.
The paint checkpoint recorded a native-size `inv-050` replay slowdown. Retain its
matching before/after evidence alongside the later CPU-context timings; measure
the remaining replay cost and representative performance before release.

**Measured result.** All 12 original dispatch controls and the 12 new solid
material/background configurations are exact. Two blue triangles and a 768-path
all-byte-alpha/color grid are exact. Six shader controls, two ellipse controls and
the separately retained rectangle alpha/color grid remain known differences.
The latter guards the axis-aligned route; the polygon grid is a separate input
identity, not a replacement reference. Native solid/pattern ellipse captures
also disprove the old same-renderer identity assertion; both failures stay banked.

| Ford size | Changed pixels, pre-paint → paint/window → one-chord → clipped → CPU context → horizontal wrapper → path mapping → numeric reading → path-point density (transparent / white / black) | Transparent alpha error, pre-paint → paint/window → one-chord → clipped → CPU context → horizontal wrapper → path mapping → numeric reading → path-point density |
| --- | --- | --- |
| 250×100 | 592/576/469 → 268/268/230 → 222/222/185 → 0/0/0 → 0/0/0 → 0/0/0 → 0/0/0 → 0/0/0 → **0/0/0** | 2,842 → 1,945 → 1,457 → 0 → 0 → 0 → 0 → 0 → **0** |
| 500×200 | 1,200/1,186/945 → 550/548/476 → 433/432/364 → 0/0/0 → 0/0/0 → 0/0/0 → 0/0/0 → 0/0/0 → **0/0/0** | 6,761 → 5,092 → 3,176 → 0 → 0 → 0 → 0 → 0 → **0** |
| 1000×400 | 5,729/5,670/5,173 → 5,194/5,249/4,739 → 5,194/5,249/4,739 → 4,900/4,956/4,404 → 929/1,245/560 → 174/174/159 → 137/137/123 → 10/10/7 → **0/0/0** | 57,568 → 56,482 → 56,482 → 51,395 → 3,573 → 2,835 → 2,734 → 16 → **0** |

At the paint checkpoint, the retained cubic twin falls from 56 changed pixels /
120 alpha error to two / six; the cubic crescent and two quadratic controls also
improve. The scan-window repair makes all four exact. No compared changed
oracle-backed fixture/research case or Ford variant has a 0-to-255/255-to-0 alpha
flip. The result is not uniformly better: 15 circle/arc/curved-stroke cases gain
alpha error, including scaled-circle 373 → 374 changed pixels and 8,644 → 8,691
alpha error. Their new candidate identities and complete comparisons remain known
failures. The design record retains both directions; no tolerance or reference
regeneration is allowed. Group-opacity-white still differs on 16 pixels.

**Landed window boundary and remaining dependency.** The matched native source
uses path bounds for dispatch/storage and the actual clip for run walking. The
mask route additionally tightens its window to path bounds. The shared renderer
now separates these values for raw edges whose complete path/control bounds fit
inside the clip. Path-sized buffers and mask/run dispatch stay intact. All 18
independent phase/scale/window/material configurations are exact; five existing
research controls graduate. Unconditionally applying clip bounds regresses three
previously exact clipped rounded-rectangle backgrounds: native clips edges before
walking paths that cross the clip. The later shared clipped preparation preserves
that return witness and uses construction bounds after clipping. The window repair
establishes neither general clipping nor a leading Ford cause: all nine Ford
complete PNGs are unchanged.

The 713-case pre-bank comparison changes 22 PNGs: 13 known fixtures, five research
controls now exact and four Default inventory cases without matching oracle
evidence (`inv-009`, `inv-017`, `inv-032`, `inv-094`). All prior exact PNGs and
dispositions are preserved. Nine known fixtures gain small amounts of alpha
error; four improve. Their complete current comparisons and original references
remain banked, with zero opaque/hole flips in every changed oracle-backed case.
The design record retains both directions and the rejected unconditional model.

**Landed cubic admission boundary.** The native builder consumes every retained
cubic verb independently of how many chords a flattening would produce. Both
managed substitution paths incorrectly required at least two chords; they now
retain one-chord cubics too. Every cubic setup attempt, including an empty piece,
is charged. Existing complete-span reservations cover the extra edges; no buffers
or limits change. Two focused mask/run cases fail when the old gate is restored.
The independent shoulder changes from eight pixels / 228 alpha error to exact;
its fractional phase remains 48 pixels but improves from 278 to 50 alpha error.
Three paired translucent/background holdouts change from 14 pixels each to exact.
Stem controls remain known differences at 6/6/4 pixels, and exact neck/scaled
controls stay exact. The bank retains all eight exact and four failing controls.

Six Ford variants improve without opaque/hole flips; all three large PNGs stay
unchanged. The small/intended geometry has seven nonhorizontal one-chord cubics,
while the large geometry has only two horizontal one-chord cubics. The first
intended-size mismatch at (495,60) belongs to a two-chord tip and is unchanged.
This establishes a shared cubic fault, not an all-nine Ford repair or wordmark exit.

**Demonstrated construction dependency (before integration).** Compiled pinned
code matches the uncut tip's setup/advance at all three sizes and the scan/coverage
operations from identical prepared edges. Ford's first path nevertheless has a
control at Y=200.667 on the intended 200-pixel canvas, so the native builder clips
the entire path. Its clipper splits cubics at Y and X extrema, including this tip
inside the clip. The managed builder splits only at Y extrema. Replacing just
the tip with its two native clipped pieces changes diagnostic alpha at (495,60)
from 129 to the oracle's 128. Diagnostic native verb construction also produces the
oracle's 212 at (247,30), versus the renderer's 229, and 111/53 at large-size
(989/990,120), versus 114/66. These are measured construction witnesses, not a
landed renderer repair or full-image/all-nine evidence.

**Kernel foundation verified (`57c6a7e3`).** Typed clip bounds, float points and ordered
line/quad/cubic pieces now preserve native X/Y chopping, root search, projection
and winding in caller-owned scratch. All 160 compiled-native cases match: 159
inputs were declared before implementation, and one targeted root-search witness
was added and independently captured afterward. Disabling cubic X chopping fails
38 of the original 159 cases. Work is checked before numerical phases and root-search
iterations, and cancellation preserves its token. At that checkpoint the kernel was not yet
on the renderer's path, so no existing pixels or resource ledgers changed.

**Shared clipped preparation landed.** An explicit construction clip and remaining
setup-work limit cross the mapped-contour boundary for every paint consumer.
Existing contour/contact and curve-retention gates remain. Two charged passes use
caller-owned stack scratch: the first counts actual emitted verbs and reserves
complete live/pre-stepped edge expansion, list growth and final arrays; the second
constructs edges. Empty clipped curves stay empty. Projected vertical lines preserve
native cancellation/trimming. Run walking uses construction bounds; mask walking
intersects those with path storage. Fractional coverage remains per paint operation.
Nine focused tests cover a native alpha witness, empty projected winding, work and
memory denial, cleanup, token identity and six-output parity across all four paints.
Disabling clipped preparation changes the independent scaled-tip alpha 168 to 192.

All six small/intended Ford variants are exact against their original captures.
The three large variants improve; their transparent alpha error falls from 56,482
to 51,395 at that checkpoint. Its nine candidate identities, references and capture provenance
are committed under `tests/References/inventory/` and exercised by ordinary tests.
One of the 27 predeclared clipping configurations graduates to exact. The other
26 retain failures; the quadratic clip phase gains alpha error (4,012 → 4,242),
and the dashed-cubic research probe gains 579 → 596. The complete comparison records
both directions and zero opaque/hole flips. Eleven changed Default inventory
outputs lack matching oracle evidence; their fidelity remains unmeasured.

**Observed CPU raster dependency.** A fresh traced capture at each transparent
Ford size is byte-identical to its original frozen reference. Small and intended
use one bitmap tile (256×128 and 512×256); large uses eight 256×256 tiles at
X=0/254/508/762 and Y=0/254, with scale 1 and one-pixel borders. The release-tag
Chromium tile calculator, border geometry and raster playback explain those
contexts; this is observed recipe-3 Windows CPU behavior, not a cross-platform
invariant. No original reference or large candidate changes in this investigation.

At row 6 of the second path, native whole-canvas construction and the managed
scanner produce the same 34 operations. Merely forcing full-canvas clipping or
extending the native walk to the path's full height leaves the mismatch unchanged.
The observed construction clip (254,0)–(510,256) changes the native trapezoid's
lower-right X and inverse slope. With native pending-run snapping, all 254 pixels
in its row-6 interior match the original alpha, including (295,6): 55 → 54.
An ordinary explicit-context test proves that the existing managed clipper also
matches that entire interior; removing construction clipping fails at 54 versus 55.
Portable trace summaries and native witness values are retained beside Ford's
original variant bank. This establishes the first construction dependency, not a
full-image repair, native authoring/map parity or a new exact-document count.

**CPU raster context checkpoint.** The shared contour/paint boundary now retains
whole-path mask/run dispatch, bordered bitmap construction clips and exclusively
owned interiors. Complete vertex/control bounds decide raw versus clipped setup.
A borrowed cache classifies geometry/contact once; contexts charge visits and
construct/reserve their own edges sequentially. Row storage intersects the bitmap
without changing whole-path dispatch. The first whole-width-buffer trial exceeded
`inv-050`'s Default work limit; bounded storage preserves the same pixels and
restores its admission at 73,302,547 work units. Limits are unchanged.

Fifteen CC0 controls were declared before implementation: thresholds, border,
phase, curve, four materials, explicit clip, layer and stroke. The two-child
pattern rejects UnsupportedFeature; that input and its original captures remain.
A subsequent admitted one-rectangle pattern holdout exercises the fourth consumer.
All 48 captures are healthy, cleaned and byte-stable in triples. Four controls are
exact, eleven retain known differences, and one rejects. The curved-stroke count
worsens 1,646 → 1,650; shader and layer residuals also remain recorded.

The 772-case pre-bank comparison preserves all 495 exact complete PNGs, every
fixture/research PNG and every disposition. Twenty-nine Default inventory outputs
change without matching captures in that sweep; their fidelity is unmeasured.
The updated bank executes **788 = 629 fixtures + 67 research + 92 Default cases**,
with all-nine Ford checked separately. CPU contexts reduce large Ford differences
4,900/4,956/4,404 → 929/1,245/560 and transparent alpha error 51,395 → 3,573
(159 over-covered / 770 under-covered pixels, no opaque/hole flips). The six exact
variants retain complete PNG identity. The converter now reproduces the original
row-6 owned interior; suppressing CPU construction clipping fails at alpha 54/55.

Context/threshold tests, cached-work denial, memory denial, cancellation-token
identity, cleanup and six-output/four-paint parity pass. Fresh locked restore,
nine-project Release build, all 3,194 tests, 25 memory scenarios and package/
installed-consumer checks pass. Local timings and allocations are recorded in
[the benchmark record](docs/BENCHMARKS.md): tiled dense/native-stop runs are faster
in the first measured order but allocate more total bytes. No representative
throughput, Linux or corporate-cohort rate is claimed. Full ignored comparisons,
ledgers, capture triples and negative control are under
`artifacts/cpu-raster-context-20261005/`; declarations, references, policy/source
provenance and regression tests are portable.

**Horizontal clip paint checkpoint.** Actual browser-recorded authored floats
and the pinned native clipper/walker still emit pair coverage 91 at (740,30).
The earliest demonstrated fault is downstream routing: `SkScanClipper` installs
`SkRectClipBlitter` for horizontal raw path-bound clipping. Its inherited pair
method forwards ordinary runs, producing premultiplied (0,18,43,91), versus the
unwrapped pair's (0,18,42,90). Vertical-only clipping and contained paths retain
specialized pairs. The renderer caches complete horizontal vertex/control bounds
once, charges the added control traversal and retains this route for each context.
No additional buffers or limits are introduced; shader and black arithmetic retain
their existing specializations. Native source identity/notices and 3,072 compiled
selection/material vectors are portable, alongside the actual recorded browser
path commands and CTM.

Thirteen original CC0 sources / 18 configurations were declared before
implementation. All 54 fresh-process captures are healthy, cleaned and stable in
triples. Fifteen solid controls are exact; three shader controls remain known
differences. Seven existing known fixtures graduate, as does one research probe
with its original PNG/provenance. All 499 previous exact complete PNGs and every
render/reject disposition are preserved. The 788-case comparison changes 48 PNGs:
22 oracle-backed fixture/research outputs and 26 Default inventory outputs without
matching captures in that sweep. Quadratic clip phase/scaled alpha error grows
4,242→4,245 / 652→655; these failures remain recorded. The updated bank executes
**806 = 648 fixtures + 66 research + 92 Default cases**, with Ford checked separately.

All six exact Ford PNGs retain identity; large differences fall
929/1,245/560→174/174/159 and transparent alpha error 3,573→2,835
(160 over-covered / 14 under-covered, no opaque/hole flips). Suppressing horizontal
wrapper routing fails the native-vector and public row-30 tests at alpha 91/90;
source bytes are restored. Fresh locked restore, nine-project Release build,
3,198 tests, 25 memory scenarios and runtime/symbol package plus installed-consumer
checks pass. Matching work/peak and both timing orders are recorded in
[the benchmarks](docs/BENCHMARKS.md); full ignored evidence is under
`artifacts/clip-pair-route-20261005/`. Native authoring/map parity, lower-tile event
fidelity, the all-nine wordmark exit, representative performance and S17 remain open.

**Axis-aligned path-mapping checkpoint.** The native row-269 comparison falsifies
intersection density as that witness's cause. The exact pinned `SkMatrix::Scale_pts`
with native `SkVx` supplies 180 output-bit vectors across four maps and point counts
one through nine. The common path-lowering boundary now uses these float lanes
only for axis-aligned maps; source numbers, relative/smooth updates and general
affine maps retain doubles. No buffers or runtime dependencies are added.

Four CC0 sources / twelve background configurations were declared after the
initial mapping trials and before production edits. All 36 fresh-process captures
are stable, healthy and profile-cleaned; all twelve configurations are exact.
The positive million-unit translation and mirrored families improve, while their
float-baked and fractional-relative controls retain exact PNGs. The 806-case
comparison preserves all 648 previous fixture PNGs, all 66 research results,
all 522 exact PNGs and every disposition. Eleven Default inventory PNGs change
without matching captures in that sweep. The updated bank executes
**818 = 660 fixtures + 66 research + 92 Default cases**; Ford is checked separately.

Six exact Ford PNGs retain complete identity. Large differences improve
174/174/159→137/137/123 and transparent alpha error 2,835→2,734, with 126 over /
11 under pixels and no opaque/hole flips. Restoring the exact old double map fails
both the independent native-vector test and the original (375,269) pixel test.
Fresh locked restore, nine-project Release build, all 3,202 tests, 25 memory
scenarios and runtime/symbol package plus isolated installed-consumer checks pass.
Matching work/peak and both timing orders are retained in
[the benchmarks](docs/BENCHMARKS.md); dense charged work grows about six percent.
Full ignored evidence is under `artifacts/lower-tile-count-20261005/`.
This does not establish full mapping parity, all-nine Ford, representative
performance, the corporate cohort, Linux or S17.

Subsequent slices must preserve all exact fixtures, output API parity, existing
admission and categorized failures, cancellation/ownership behavior and Default
resource limits. Record complete candidate/reference identities, total and
directional alpha errors and opaque/hole changes for every affected banked image.
Run full fixture/research and available inventory variants, with missing captures
and unmeasured variants listed explicitly. The legacy 92-file Default sweep is
not full independent-variant validation; those gaps cannot shrink a denominator.
Measure matching before/after tiny/Ford/dense/native-size time, managed allocation,
charged work and peaks; local timings do not replace representative throughput.

The two selected exits are established: all nine Ford and all nine YouTube
variants are exact. Preserve raw classification, AA dispatch, triangle coverage
and byte clip arithmetic; proceed with the measured cohort and remaining shader,
clip and curve arithmetic above.
S17 still needs the exact-document threshold
and the delivery evidence described above. The complete cohort's measured
blockers order later shared work; the eight holdouts remain evaluation inputs.

## 4. Findings and stage ledger

### Active findings

| ID | Current disposition and remaining closure |
| --- | --- |
| R26 | First-cohort selection and measurement complete: all 27 documents / 258 variants, including eight fresh holdouts and eighteen rejections, have browser references. Exact documents are 4/27; broader representativeness and S17 remain open. Historical exact labels and the legacy 92-file Default bank are not S17 rates. Preserve failures and original assets. |
| R27 | Every fixture is executed. Still retain complete decoded candidate identity/diff, validate historical/current oracle identity, distinguish render/reject from fidelity, and graduate positive research controls instead of deleting them. |
| R28 | Original pattern/root-map repair and inverse gradient maps retained. Paint/alpha one-step residuals remain S11/S13. |
| R30 | Shared contour preparation, ownership and coverage traversal have landed across the four paints. Preserve that boundary while changing curve state; remaining repeated binding/typed-state work follows demonstrated dependencies, not a repository rewrite. |
| R31 | CI artifact-validator hooks, Windows sandbox setup/health, wrapper deadline/drain and atomic publication are repaired. Opt-in corporate capture and VisualDiff cohort reporting now cover the entire frozen matrix. Representative performance and remote platform evidence remain. Git identifies code; input/output/browser hashes protect the measured artifacts. |
| R33 | Original scaled clip-coordinate fault repaired; fractional mapped coverage and exact independent controls exist. Contained raw edges use a distinct scan window. Escaping control bounds trigger native clipped construction, even when the tip fits: this is demonstrated on Ford. Construction clipping is integrated; the separate AA paint-clip dispatch and mask-bound provenance contract is validated with native/browser, resource and delivery evidence. Full AA masks/cache intersections remain open. Preserve exact controls and Ford while resolving general clip/event combinations. |
| R34 | Shared scan conversion, analytic coverage, contact/union, straight-source sibling-stroke separation, operation/material preservation, contained-edge scan windows and one-chord cubic admission have landed. Clipped construction followed by path-point density now makes all nine Ford variants exact; uncut tip recurrence and sampled existing-edge operations match compiled native code. Complex parity/contact cases and curved/dashed stroke unions retain their gates or known differences. Preserve the exact twin-ring repair; rows 4/6. |
| R35 | Retained controls, device-map threading, live non-convex cubic fills, curve/walk state separation and one-chord cubic admission have landed. Axis-aligned float mapping closes (375,269) and preserves independent large translations. Shared numeric reading and relative/smooth arithmetic have also landed: they match all 436 recorded authored points and the actual large CTM and close (269,187) on the original input. Earlier injected/mapping discriminators retain their historical limitations. The independently demonstrated lower-row path-point density decision has landed with row 4 and closes all ten residuals. General tile-local/affine/instance mapping, fractional events and quad/convex/stroke migration remain bounded follow-ups; row 5. |
| R36 | Cascaded cap/join/dash/width values are carried and many spelling twins pass. Finish shared typed state and practical inheritance without reopening repaired attribute/inline cases. |
| R37 | Fractional/scientific curves, root sizing, relative-moveto repairs, similarity maps and arc-free path-fill skew admission have landed. Preserve the support matrix's remaining arc/stroke/instance and general-transform gates; new admissions require the active target's dependency evidence. |
| R38 | Maximum scratch allocations substantially reduced; continue actual-size storage, ownership transfer and measured many-curve/wordmark allocation work. |
| R39 | Mapping/definition lifetime and shared preparation reservation repairs are retained. Audit each new curve-state buffer, growth peak and setup/advance/coverage cost before allocation or work; preserve during-phase cancellation and cleanup. Passing existing memory scenarios does not prove every new allocation bounded. |

### Original finding dispositions and retained regressions

| IDs / stages | Disposition |
| --- | --- |
| R01/R02 | Glyph/contour visibility, sibling/CDATA/empty-text and XML-tail fixes retained. |
| R03/R08/R10 | Original shared style/admission and selected text/outline semantics repaired; wider text is S7/S15. |
| R04/R12; S1–S5 | Original sizing, cleanup, glyph ownership, numeric/aggregate work and XML workspace/copy/membership repairs retained. New geometry work is R38/R39. |
| R05/R06/R07/R09/R10/R11/R16 | Original matrix/cmap/face/identity/lifetime/diagnostic repairs retained. Ordinary-font fidelity is not thereby proved. |
| R13; S7 | Intended-font metrics/pixels remain open; synthetic glyphs and source access do not close them. |
| R14/R15/R19 | Sharing/cache/typed-size/allocation repairs retained; remaining consolidation is R30/R38. |
| R17/R18 | Discovery reservation and original eligibility/root-count parity repaired. |
| R20/R21; S6 | PNG first-write/output limit, common encoding kernels and disposable single-use prepared-scene lifetime repairs retained. |
| R22/R23/R31a | Original provenance, pixel comparison, CDP transport/cleanup and later sandbox startup/diagnostics/deadline repairs retained. |
| R24/R25 | Channel-wrap and definition-ancestor transform defects repaired. Residual alpha and broader inherited semantics have their active owners. |
| R27 original branch | Manifest now executes both renderable and rejected unsupported entries. Remaining fidelity reporting is above. |
| R29/R32 | Decoder reserve-before-copy, proportional image/tile work, shared gradient stops, corrupt-zlib categorization and critical-chunk policy repaired. Per-wrapper granularity and lenient late tRNS remain recorded limits. |
| R40 | Release-tag source provenance and opaque coverage calibration corrected. Preserve the matched pin and operation-specific trace; remaining numerical fidelity is S11, not an open provenance defect. |
| R41; S12 bounded repair | Zero-area objectBoundingBox gradient path strokes and the miter-threshold branch are repaired. Preserve path/use, userSpace and acute-limit witnesses; row 1 is complete. |

### Stage completion criteria

| Stage | State / remaining exit |
| --- | --- |
| S1–S5 | Core foundations and original repairs exist; preserve API, parser, memory/work and output contracts throughout. |
| S6 | Disposable single-use preparation exists; preserve resource leases and six-output behavior. |
| S7 | Generic local/stdin/HTTP supplied-font host exists; actual-pack and same-font metrics/pixels remain separate work under section 6. |
| S8 | Complete for the first frozen corporate cohort: 27 documents / 258 variants including eight fresh holdouts, all renderer cases executed, all references available and reusable capture/reporting tools. Results are 70 exact variants, 170 known differences and eighteen rejections; exact documents 4/27. Selection limits remain explicit; S17 requires the exact-document threshold and delivery evidence. The historical 92-file bank stays separate. |
| S9 | Styles/references/viewports, declaration-only DOCTYPEs and measured inventory admissions are implemented in explicit subsets. Remaining combinations follow independent corporate-cohort evidence and shared semantics. |
| S10 | Full path-command/affine kernels, retained controls, live non-convex cubics, curve/walk state separation and one-chord cubic admission exist. Raw-control classification is integrated; fractional-event fidelity, remaining transform/instance gates and fallback migrations need per-input evidence. |
| S11 | Critical path: general Chrome-exact coverage/curves/composition unproved. Require exact held-out phase/scale/paint/alpha families. |
| S12 | Caps/joins/dashes/stroke outlines and the bounded row-1 repairs exist. Remaining curved/dashed union, tangent and antialiasing errors belong to row 6 using common coverage. |
| S13 | Gradient/pattern paint maps and premultiplied composition exist; R41 is closed. Bounded full-canvas paint routing is repaired in row 3; remaining coverage/paint alpha, dither, group composition and paint combinations need exact controls. |
| S14 | Rectangle clips, contained-edge scan windows, limited masks and isolated opacity exist. Bounded CPU raster contexts separate bitmap construction bounds from owned interiors. Clipped construction is integrated when path/control bounds escape the bitmap, canvas or explicit clip, including curves whose geometry fits. AA paint-clip dispatch is integrated for contained and clipped paths with geometric boundary provenance separate from mask allocation regions. The bounded predicate/setup and paint-context proof excludes full native AA masks/cache intersections. Preserve X/Y pieces, projected winding, whole-path dispatch, bitmap-bounded storage, exclusive interiors and fractional paint coverage. General clipped event fidelity, clips, mask regions/content and bounded nested composition remain open. |
| S15 | Live-text wordmarks are experimental, not required for outlined wordmarks. Follow section 6 and shared glyph contours, not a separate renderer. |
| S16 | Uniform embedded PNGs and narrow patterns exist. General raster decoding/resampling, multi-element patterns, markers, filters/blending and additional static CSS only for measured blockers. |
| S17 | Not achieved. The first cohort is frozen; it needs at least 11/13 exact marks and 12/14 exact wordmarks (at least 23/27 total), every declared variant exact for counted documents, and fresh delivery/resource/performance evidence. Report its selection limits; broader representativeness remains unproved. |

## 5. Product contract, evidence and completion

Keep one pure-managed C# net10.0 library for Windows/Linux. Retain the project's
MIT license and the adapted clipping code's BSD license/notices in
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt). Bounded managed source
adaptations follow section 7. Preserve zero third-party runtime packages, native
assets/PInvoke, System.Drawing, installed-font engines, browser rendering services
or other Lokad runtime dependencies. Rendering consumes
explicit options/resources; it does not discover filesystem/network resources.
Scripts/events, animation, mutable DOM, foreignObject HTML and arbitrary external
documents stay out of scope. Live fonts and effects must not delay outlined logos.

Preserve the six output APIs, owned/caller memory, overlap rejection and non-array
memory; named null arguments, categorized errors and token-preserving cancellation.
Async memory schedules CPU work once; async streams await backpressure, propagate
failures and stay open. Hosts own transactional output. Prepared documents are
single-use owners; snapshots/borrowed bytes retain their documented lifetimes.
Read [the C# guidelines](docs/GUIDELINES_CSHARP.md) before C# changes.

| Default bound | Value / interpretation |
| --- | --- |
| Renderer-accounted live storage | 256 MiB; not process/host memory. Audit every changed R38/R39 ownership/allocation path. |
| SVG input / encoded PNG | 16 MiB / 128 MiB. |
| Output | 16,384 per axis; 16,777,216 pixels. |
| Nodes / cumulative path commands | 100,000 / 1,000,000. |
| Shared conversion work | 100,000,000 units through preparation, scene, raster and encoding. |
| XML depth / attributes per element | 128 / 256. |
| Direct text | 32,768 cumulative UTF-16 characters. |
| Snapshot caps | 256 faces / 32 MiB distinct borrowed programs, separate accounting. |

Root lowering and discovery share the conservative XML allowance
`max(16 KiB, 4 × input bytes, 512 × configured attribute limit)`. Distinguish that
allowance, ledger peaks, cumulative managed allocations, borrowed storage and
process working set. Charge curve/stroke/dash/reference expansion, pair/vertex
scans, coverage/paint, images and intermediate surfaces before denied work;
check cancellation within bounded chunks and release owners on every exit.

### Validation and evidence policy

- Identify implementation with Git commits and upstream code with pinned revisions
  and URLs. Keep detailed per-run diagnostics under ignored artifacts; commit
  reusable regressions and concise reproduction details. Tests assert behavior
  and protect input/reference identity. Hash test assets and pinned external binary
  downloads where integrity is checked. Do not maintain hashes of source files,
  extracted code snippets, scripts, local adapters, solution binaries, packages,
  diagnostic reports or logs, including in ignored run reports. Refresh measured
  outcomes when behavior changes; ordinary source edits and rebuilds require no
  reference metadata refresh.
- Each implementation slice includes locked restore/Release build/tests,
  independent semantic/numeric/negative vectors and relevant documentation.
  Current exact references must remain exact. Changes to known mismatches require
  full before/after comparison and explanation, not merely refreshed maxima.
- Preserve exact controls. Graduate useful original research cases into the exact fixture
  corpus with their oracle provenance. Keep semantic failures as well as AA probes.
  Extend the existing harness rather than creating another testing system.
- Retain decoded candidate identity or complete diff evidence; mismatch count,
  maxima and bounding box do not uniquely identify an output. Verify browser,
  executable hash, recipe, platform, dimensions/options, background and input/asset
  identity against the appropriate historical/current pin.
- New references require three stable fresh-process captures with existing
  sandboxed tooling. Compare decoded RGBA8 exactly, ignoring RGB only when both
  pixels are fully transparent. Browser/recipe migration is deliberate; a failed
  candidate never authorizes replacing its reference.
- Exercise during-phase cancellation, tight remaining memory/work denial,
  cleanup after partial construction and six-output parity where affected.
  Extend existing memory/benchmark scenarios for curves, many contours, paints,
  clips and layers; do not infer performance from ledger-only observations.
- Re-render affected inventory inputs after narrow repairs; broad coverage,
  transform or composition changes require comparison across all declared
  variants. Run available variants and report the incomplete bank explicitly;
  missing/oracle-failure/unmeasured results cannot disappear from the denominator.
  Ordinary unit tests
  and library consumers remain offline and independent of browsers/private assets.
- Package/API changes require the isolated installed consumer; release claims
  require fresh Windows/Linux build/test/package/resource and platform-appropriate
  oracle evidence. A Windows reference is not automatic Linux fidelity evidence.

### S8/S17 accounting and remaining release decisions

Keep original files, hashes, source/license evidence and generator provenance when
known. Never redraw/simplify hard logos, select easier variants after failure, or
reuse retired inv-033/inv-036 IDs. Public-domain candidates are not contributor
CC0 originals; preserve their provenance and keep private assets untracked.

Turn inventory declarations into explicit options per variant: logical viewport,
outputScale, resulting dimensions and background, with input and materialized-input
hashes if capture requires materialization. Changing screenshot dimensions alone
crops a fixed-size SVG image; it does not implement scaling. The browser
transport now admits up to 8,388,608 pixels, with a screenshot-only
64 MiB reply allowance. Transport bounds never permit dropping a declared large
variant or changing the renderer's pixel limit.

Renderer outcomes and oracle availability are independent axes. The
[current measurement](tests/References/inventory/corporate-cohort-current.json)
records this cross-tabulation:

| Renderer outcome | Measured | Unmeasured | Transport gap | Total |
| --- | ---: | ---: | ---: | ---: |
| Renders | 240 | 0 | 0 | 240 |
| UnsupportedFeature | 18 | 0 | 0 | 18 |
| Total | 258 | 0 | 0 | 258 |

The 240 measured renders contain 70 exact variants and 170 known differences.
All nine Target and all nine Coca-Cola variants have browser references but retain
UnsupportedFeature outcomes; reference availability and renderability are
independent axes. The first cohort's complete reference matrix closes its S8
measurement obligation, including browser behavior for rejected inputs. The 830
exact fixture entries remain separate from corporate comparisons.

Report render/reject separately from exact/known-difference/oracle-failure and
unmeasured. A document counts only when **all declared variants** are measured
and exact. Preserve historical narrower successes without counting their unmeasured
sizes. Preserve the completed corporate-mark/outlined-wordmark selection and
holdout freeze; retain flags, diagrams and other symbols as separate useful
strata. Keep every frozen document in its denominator, including
rejects, known differences, oracle failures and unmeasured variants. Fixture-entry
percentages and the legacy 92-file Default bank cannot replace corporate-document
coverage. Ford establishes the outlined-wordmark checkpoint; YouTube is the
selected curved-mark target and must retain all nine declared cases.

The S17 target is at least 80% exact documents overall and in each of the
corporate-mark/outlined-wordmark strata (round required document counts upward).
The first corporate cohort and explicit variant recipe are frozen in the
[manifest](tests/References/inventory/corporate-cohort-v1.json). Existing renderer
outcomes did not filter membership: all 19 retained brand/organization exports
and eight predeclared fresh holdouts remain. The 22/27 rounded overall minimum
and the 11/13 mark plus 12/14 wordmark minima all apply: **23 exact documents is
the effective minimum** for this cohort. All 258 cases, including the fifteen former
transport-limit gaps and eighteen UnsupportedFeature cases (Coca-Cola's touching
contours and Target's `marker` style), retain their denominator. Combined identities
already have a declared primary stratum. Public-domain source selection and the organizational
mix limit representativeness; future cohorts must be selected before measurement
as well.
Report the measured rate with this cohort's scope and selection limits even after
it meets the target. The target alone does not justify a “most logos” market
claim; broader representativeness and the remaining release gates need evidence.
There is no separate quirk budget: matching the pinned oracle remains
implementation work, and any document
with a mismatching variant cannot enter the exact numerator. A coverage target
below 100 percent may leave failed documents in the denominator; it never makes
their pixels exact. S17 also requires categorized residuals, reproducible oracle
evidence, representative latency/allocations and fresh delivery/platform gates.

Maintain API_CONTRACT, README and SUPPORT_MATRIX as current contracts. Keep
dimension rules, admitted combinations and fixture counts aligned with the
implementation. PLAN is the execution queue; INVENTORY holds document evidence,
RASTERIZATION_DESIGN holds hypotheses/results and BENCHMARKS holds measurements.
Neither another status paragraph nor another source audit is an implementation exit.

## 6. Font, text and host requirements retained

### Shared public font-pack contract

Preserve the public Lokad.OoxPdf 0.1.4 manifest contract; the 2026-09-20 source review
of the local OoxPdf checkout at `8e4b0333` (declared version 0.1.5, not a verified
published release) confirms the same fields and resolution order. The reviewed
public files are `Fonts/OoxPdfFontPackResolver.cs`, `Fonts/FontProgramLoader.cs`,
`FontTests.cs`, `README.md` and `docs/PrivateValidation.md`. Both renderers must use
unchanged manifest/font files from one location, without a converted/pruned SVG
manifest, duplicated deployment or OoxPdf assembly/package/project dependency.
Focused independent font-management adaptation is allowed; PDF embedding and SVG
glyph rasterization retain distinct responsibilities.

| JSON location | Compatibility |
| --- | --- |
| Root | `packId`, `files`, `families`, optional `fallbacks`; requested ID validation. Convention `<source>/<packId>/manifest.json`, font paths relative to root. |
| Files | `relativePath`, `byteSize`, `sha256`; safe paths, verified selected declarations. |
| Families | `requestedFamily`, `resolvedFamily`, `relativeFontFile`, `weight`, `italic`, `faceIndex`, `hasMathTable`; aliases/multiple faces per file. |
| Omitted fields | Weight 400, italic false, face index 0, math flag false; math metadata implies no SVG math layout. |
| Fallbacks | Ordered `{ "family": "..." }`; omission means empty list; case-insensitive family/alias matching and deterministic style/selection tie-breaks. |

Preserve web-style case-insensitive properties and unknown-property tolerance;
require no new schema/license/source fields. Provenance may be a sidecar. Bound
metadata without rejecting unused unsupported-format entries; reject explicitly
when selected. Pin self-contained conformance fixtures to the public contract:
defaults/aliases/fallback order, unknown metadata, paths, unused TTC and selective
loading. No other renderer checkout is required.

The focused comparison establishes these alignment targets; source inspection of
upstream tests is not a claim that those tests were executed here:

| OoxPdf behavior inspected | Lokad.Svg alignment and remaining work |
| --- | --- |
| Manifest first; font bytes fetched lazily for selected files and checked by length/SHA-256. | Keep `SvgFontPackCatalog` metadata-only and acquire through caller-owned sources during async preparation. Verify the unchanged private manifest with Svg itself before claiming end-to-end compatibility. |
| Case-insensitive requested/resolved family matching; italic match, closest 400/700 weight, then deterministic family/path/face tie-breaks; ordered configured fallback. | Existing Svg selection follows that order and accepts numeric SVG weights. Independent conformance vectors cover aliases, four real styles, fallback (`b0d4d5d`), plus unused collection entries that parse and select without poisoning the catalog while snapshots reject selected collection faces before acquisition. OoxPdf's bold request maps to 700; preserve SVG's numeric weight semantics. |
| Shared per-file sources coalesce concurrent downloads, cache successful bytes and use at most three attempts for transient transport failures. Manifest reads cap at 1 MiB; font reads cap at the smaller of the declared size and 64 MiB. | Put transport, bounded caching and retry in an opt-in test host/provider. Test concurrent reuse, cancellation and recovery with synthetic transport responses. Keep Svg's existing 32 MiB aggregate borrowed-program and 256-face caps explicit; the source's per-file cap does not replace them. |
| Font loading can select collection faces; the inspected loader synchronously waits for bytes, copies them and can return no font for certain failures. | Retain Svg's async acquisition, immutable borrowed bytes and categorized required-font failures. Current face-0 standalone TrueType admission remains explicit; packs may contain unused TTC or other unsupported entries without rejecting the whole catalog. |
| Private validation keeps manifests, inputs and reports ignored and commits anonymized findings. Download exceptions can contain source URIs. | Apply the private workflow below and redact transport errors before they reach repository reports or shared logs. Do not propagate private source identities through diagnostic `StableId` values. |

### Authorized private font testing

User authorization covers testing with the existing deployment's blob storage and
manifest. It does not authorize committing or publishing any platform content.
Never commit the storage address, account/container identifiers, credentials or
SAS values, deployment pack identifier, actual manifest, font binaries, platform
configuration/code, or derived artifacts that disclose them. This includes
embedded-font SVG/data URLs, request/response traces, exception text and reports.
Any retained private inputs, font caches and captures belong outside this repo or
under its already-ignored `artifacts/private-fonts/`; check that they are untracked
before use. Follow OoxPdf's private-validation approach with Svg's existing ignore
layout. Ordinary builds/tests and public CI must remain offline and independent of
the private source and sibling checkouts; do not upload private test artifacts.

Historical assessment (2026-09-20; not repeated in this review): read the
configured source without printing its location; streamed the unchanged manifest
under 1 MiB and checked the requested
identifier; fetched one selected regular face-0 program under its declared size
and Svg's aggregate limit; verified exact length and SHA-256. It has the standalone
TrueType signature and `head/maxp/hhea/hmtx/cmap/loca/glyf` table entries, plus
`kern` and `GPOS`, and no `fvar` entry. These are initial format observations, not
table validation, shaping support or a whole-pack inventory. All downloaded bytes
stayed in memory. No Svg catalog/snapshot conversion, OoxPdf test run, browser
capture or pixel comparison was performed in that assessment. No new private source access was needed for the 2026-09-21
review.

Execute S7 through the following concrete steps:

1. Retain the implemented opt-in generic local test host accepting a supplied manifest/byte source
   or externally configured endpoint and pack identifier, with no real defaults
   or platform configuration discovery in tracked code. Reuse unchanged assets;
   retain private hashes/provenance locally. Share selection and source behavior
   with the public OoxPdf contract without importing platform implementation.
2. Parse the actual manifest with Svg, prepare only requested programs, and build
   a private inventory of formats, faces/styles, coverage, composite flags and
   positioning tables. Compare catalog selection with OoxPdf for representative
   requests. Report unsupported semantics explicitly; do not rewrite the pack or
   silently substitute a machine font. Turn reproducible defects into synthetic
   tracked tests and fix them under S3–S6 before admitting wider text behavior.
3. Generate small synthetic label inputs covering curves, counters, composites,
   AV pairs, spaces, real styles and fractional origins/sizes. Feed identical
   verified font bytes to Svg and an SVG-image-local browser font embedding.
   Browser captures remain network-blocked; use missing/corrupt/substitution
   controls to prove intended-font use. Retain three fresh-process captures,
   independent layout metrics and exact RGBA comparisons in private artifacts.
4. Record sanitized failure categories, feature gaps and measured outcomes here.
   A private mismatch must remain visible even if public tests pass. Private
   success may support local investigation, but public corpus/package admission
   needs independently redistributable fixtures and reproducible public evidence.

The requirements below mix implemented foundations and remaining targets. A
single-use retained prepared document exists (R21's lifetime defect is repaired);
general family/coverage fallback and shaping are not thereby implemented.

Hosts own locations, credentials, I/O/retry/concurrency and bounded shared caches;
the library owns SVG analysis, ordered family/generic mapping, face selection,
coverage fallback, layout and painting. Prefer real styles; no implicit synthetic
styles or machine fallback. Record substitutions and deliberate policy differences.
Scalar fallback is not complex shaping.

Bounded async preparation acquires only required resources and retains admitted
parse/style state plus byte leases in a disposable prepared document, initially
with explicit single-conversion ownership and shared limits through output.
Raw snapshots may have a separately specified concurrent-read lifetime. Never
synchronously block on acquisition. Avoid unconditional font copies/unbounded
global caches. Identity is hash plus face index; caller bytes stay valid/unchanged.
Shape-only input works offline; required missing faces/glyphs cannot disappear.

Core bundled/required font bytes remain zero. Count distinct programs rather than
aliases/faces and measure acquired bytes separately from parsed storage. Keep
deployment inventories and private program identities out of this plan. Select
redistributable public test fonts separately, retaining their asset licenses.

### First useful text milestone

Horizontal LTR Latin labels, initially ASCII/tested precomposed Latin, using supplied
static standalone TrueType face 0. Validate SFNT/ranges, `head`, `maxp`,
`hhea`/`hmtx`, `cmap` 4/12, `loca`/`glyf`, plus optional `name`/`OS/2` metadata
structures.
Include simple curves, implied points and bounded composite placement; gate
unsupported formats/flags.

Preserve text/CDATA/span order and admitted whitespace across boundaries. Support
inherited font/paint/visibility, scalar x/y/dx/dy, alphabetic baseline,
start/middle/end anchors, advances and required pair positioning (`kern` and
explicit bounded GPOS pairs). Test AV, spaces/punctuation, accents/composites,
supplementary scalars, mixed faces/sizes/colors and fallback boundaries.
Implement or reject features affecting output. Add styled/positioned `tspan`, spacing and
explicit line positioning for labels.

Position/rotate lists, `textLength`/`lengthAdjust`, more baselines, transformed/
stroked text and decorations need later gates. Initially exclude automatic wrapping,
bidi/joining/complex scripts, unadmitted mark positioning, general GSUB/GPOS, text
paths/vertical text, variable/color/bitmap fonts, CFF/CFF2 and WOFF/WOFF2.

### Host/oracle/corpus acceptance

Demonstrate a standalone host with async bounded nonseekable stream intake,
preserved XML/BOM bytes and growth-time input limits; a public stream-input overload
is not initially required. Host buffers/shared caches are separate budgets. Bound
concurrent loads, isolate caller cancellation and allow retry. Output awaits
backpressure, propagates failures and leaves streams open; hosts own partial-output
cleanup/transactional publication. Include real labels, not only transport tests.

Retain SVG and font hashes, face index, materialized oracle-input hash, browser/
platform/version, recipe, scale, locale/background and font/raster policy. Verify
font use inside the SVG image with missing/corrupt/substitution controls; outer-page
font readiness is insufficient. Require three stable fresh-process captures.
Compare metrics/layout separately from pixels and keep candidates/references/diffs,
bounds and classified findings. Private evidence stays in the authorized local
workflow above and must not enter public reference manifests or CI artifacts.
A box glyph or single size is insufficient.

Keep exact decoded RGBA8 comparison, ignoring RGB only for transparent pixels.
No tolerance or silent reference regeneration. Separate support and fidelity
(`exact`, `known-difference`, `oracle-failure`) in explicit inventories.
Preserve rejected diagonal/overlap/opacity models as negative evidence; Windows
oracle results are not canonical Linux evidence. Work toward 200 targeted cases and
10–20 representative diagrams/illustrations as coverage prompts, not release quotas.

## 7. Optional reference sources

The local reference review in the earlier draft remains useful design guidance.
Keep `external/` optional, ignored and unreferenced by builds, tests, packages and
runtime: no submodule, CI download or hidden Rust/C++/Node dependency. Do not modify
another worker's reference checkout to test its absence. Ordinary
build/test/package/consumer workflows need no sibling checkout.

Permit bounded C# adaptations of numerical algorithms needed by a demonstrated
renderer dependency, as with the clipping kernel at `57c6a7e3`. Record the exact
source revision/files, retain copyright and license notices in source and packages,
and verify independent vectors plus the shared ownership/work/cancellation
contracts. Adaptations must remain self-contained managed code with portable
regressions. Whole-renderer vendoring or translation remains outside the plan.

| Inspected source / revision | Bounded use |
| --- | --- |
| resvg `75b6bbadd7999d0516dcd7153b4a321bfdf8670a` | `usvg` style/tree, use/clip/paint binding and renderer operation order; typed resolved state and distinct geometry/stroke/layer bounds. |
| tiny-skia `5d4754777746eef0828be166896eaf482c49f8f2` | Edge/span organization, compact paths, stroke/dash expansion and storage reuse. Its fourfold supersampling is not Chrome-exact analytic AA. |
| resvg-test-suite `d8e064337faf01bc5a9579187a56dbdbe3eacc72` | Semantic counterexample index. It overlaps heavily with resvg's bundled suite; neither is a separate real-logo cohort or our PNG oracle. |
| NanoSVG `239e102ec2c691f2902e20ace2ed36ee4a35cfe6` | Secondary small-parser/scanline comparison only; five-subsample output is not the exactness target. |
| Matched Skia `f8b66b7597c4cc859d3ed190e9c6872241e6721c` | Release-tag scan, paint routing, stroker and curve construction for demonstrated S11/S12/S14 dependencies. The bounded clipping/solver adaptation retains this pin and BSD notices; compiled-native vectors and experiment provenance are in the design record. |

The recorded Skia comparison `8b8c3872fbc03f025855db96ce683f34ec98a815`
is a main revision, not the browser release pin. Use explicit matched
source reads without changing it implicitly. resvg's registry tiny-skia dependency
also need not be byte-identical to the sibling checkout despite equal version text.

Historical source map (inspected 2026-09-29; paths are relative to each optional
checkout and are study references only):

- **Row 4:** tiny-skia `src/scan/path.rs::walk_edges` shows active-edge ordering,
  winding/parity and span emission. Its `src/scan/path_aa.rs` uses supersampling;
  use it for organization, not the Chrome numerical target. At the matched Skia
  revision, inspect `src/core/SkScan_AntiPath.cpp` for dispatch,
  `src/core/SkScan_AAAPath.cpp::aaa_walk_edges` for scan intervals/coverage, and
  `src/core/SkAnalyticEdge.cpp::setLine` for edge quantization. Source paths alone
  do not prove the browser's selected fast path; retained captures adjudicate.
- **Rows 5–6:** resvg `crates/usvg/src/tree/mod.rs::Path` retains path data, paint
  and transform/bounds separately; tiny-skia `path/src/stroker.rs::PathStroker`
  processes line/quadratic/cubic verbs and curve normals. Apply those boundaries
  within the existing C# ownership model; bounded numerical adaptations follow
  the policy above.
- **Row 7:** resvg `crates/resvg/src/render.rs::render_group` isolates bounded
  layers and tracks their origin; sibling `clip.rs` and `mask.rs` reuse traversal
  for effect content. Their composition pixels still need our pinned oracle.

Read a matched Skia file with
`git -C external/skia show f8b66b7597c4cc859d3ed190e9c6872241e6721c:src/core/SkScan_AAAPath.cpp`.
Do not switch the shared checkout or add a reference-source dependency.

Upstream success criteria and defaults are not ours: resvg tests allow channel
tolerance and can regenerate references from themselves; the test-suite Chrome
helper disables the sandbox; upstream resolvers may read host files or keep a
malformed path prefix. Keep our pinned captures, explicit providers, categorized
failures, strict comparison and bounds. Retain upstream notices with optional
checkouts and the required notices for adapted product code; bundled fonts/assets
have separate licenses. Source access does not authorize whole-renderer vendoring
or publication of private assets.

Use the existing [oracle tooling](docs/ORACLE.md),
[research record](docs/RASTERIZATION_DESIGN.md),
[comparison tool](docs/VISUAL_COMPARISON.md),
[inventory](docs/INVENTORY.md) and [benchmarks](docs/BENCHMARKS.md).
Do not build Chromium, fetch a renderer ecosystem or survey more reference
implementations as a prerequisite to the concrete queue.
