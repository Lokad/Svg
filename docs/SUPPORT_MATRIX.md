# Support matrix

## Current state

The renderer supports the explicit subsets listed below. The original retained
rendering differences are resolved; the original-scope admission and feature
gaps in [PLAN.md](../PLAN.md#3-ordered-support-queue) remain open. Do not infer
that every unlisted visible SVG construct is rejected correctly.
The original fixture manifest's 1,410 entries contain
1,396 exact decoded-pixel references, zero known-difference renders and 14
categorized rejections. These are fixture entries, including variants, and do not
establish corporate-document coverage.

Native nonzero stroke coverage retains zero-signed-area contours carrying local
directed winding, through single/multiple and clipped/unclipped preparation.
The final contained-ring fixture is exact. Mixed closed/open-subpath strokes with
linear, radial or pattern paint still hit an existing admission restriction; nine
retained browser controls make that support gap explicit. See [the repair](RASTERIZATION_DESIGN.md#zero-area-native-stroke-winding-2026-10-08).

The [four whole-export failures](../tests/Research/finite-support-exports/manifest.json)
completed the preceding document queue at **22/22**, separately from controls
and the corporate **27/27 / 258/258** matrix. The spiral, tunnel junction and
shadowed soccerball now match unchanged outputs through six APIs on Windows/Linux.
The soccerball reuses mask container inheritance, shared oval shader fill/stroke
lowering and enclosed mask paint-clip dispatch; its 33 separate controls are
exact, with 198 healthy matching captures. Sixteen earlier mask-scope controls
stay exact. The transparent striped pattern `inv-116` is now exact through six
APIs on both platforms. Twenty-nine focused controls protect typed paint order,
instance inheritance and pattern-container presentation, with 174 healthy matching
captures. Existing affine tile mapping, clipping and opacity stay exact.
The fractional use-width gate and non-scaling root-viewBox width defect remain
open under G2/G3/G4. That correction passed both Release builds and **6,072 tests/platform**, with
all 2,908 prior reports and 2,846 PNGs unchanged under complete qualification.
Original sources, declared frames and frozen references remain unchanged.

Both retained [baseball documents](../tests/Research/compound-clip-documents/manifest.json)
are complete, closing the preceding queue at **24/24**. Their unchanged 130×130
and 1200×800 transparent references are exact through all six APIs on Windows/Linux.
Shared compound clipping, percentage rectangles, affine shader maps, native union
traversal and local transform-list precision remove the observed gates and edges.
Separate browser controls retain **17 exact / one explicit rejection**; four
numeric contracts protect transform precision, and earlier viewport-height controls
remain passing. Finished complete qualification passes **6,118 tests/platform**
(4,350 core / 1,762 visual / six FontProbe), preserving all 2,846 prior PNGs
byte-for-byte, all 2,752 prior exact results and corporate **27/27 / 258/258**.
The [two CC0 arrow documents](../tests/Research/marker-documents/manifest.json)
complete the preceding queue at **26/26**, exact through six APIs on both
platforms at unchanged 2000×500 and 800×400 transparent outputs. Bounded marker
content reuses symbol/group/use and native line-stroke replay. Fifteen exact
controls and five explicit boundaries protect units, orientation, source paint,
forward cascade, opacity and binary viewport clipping; 120 healthy matching
captures keep the references stable. Three controls sweep work/memory denial,
cancellation and retry. Two inert line-fill controls graduate against existing
references. Complete Release qualification passes **6,143 tests/platform**
(4,353 core / 1,784 visual / six FontProbe), with zero failures/skips/warnings,
all 2,847 prior PNGs unchanged and all 2,753 prior exact results preserved.
Corporate **27/27 / 258/258** remains frozen. The
[two original CC0 path-marker exports](../tests/Research/path-marker-documents/manifest.json)
complete the selected development queue at **28/28**: `inv-123`, 512×352, and
`inv-124`, 512×512, transparent, through six APIs on Windows/Linux. Original bytes,
options and references remain untouched; their twelve healthy matching captures
are reused. Shared authored path vertices and tangent placement, visible marker
overflow attributes and bounded content replay remove both documents' gates.
Twenty exact controls and three boundaries have 138 healthy matching captures;
three source controls sweep denial, cancellation and retry. XML `marker`
shorthand attributes are inert; CSS shorthand and longhand attributes activate
markers. The earlier 20 marker controls now retain 16 exact / four boundaries.
Complete Release qualification passes **6,171 tests/platform** (4,356 core /
1,809 visual / six FontProbe), with zero failures/skips/warnings. All 2,847 prior
PNGs remain unchanged and all 2,753 established exact results stay exact.
The original CC0 `original-logo-marker-mark` is also complete at unchanged 64×64
transparent through all six APIs on Windows/Linux, counted separately from the
30 development documents. Its [instance bank](../tests/Research/marker-instance-content/manifest.json)
has one complete acceptance document and 15 controls: 12 exact / three unsupported
boundaries, with 96 healthy matching platform captures. Three source-owner cases
sweep denial, cancellation and retry. Shared retained presentation preserves marker
cascade and definition paint; source viewport clips move with their geometry,
and binary opacity restoration preserves native paint arithmetic.
Current complete qualification passes **6,295 tests/platform** (4,425 core /
1,864 visual / six FontProbe), with zero failures/skips/warnings. All 2,849 prior
PNGs and 2,755 established exact results remain unchanged; the original oracle
keeps 1,553 PNGs from 1,567 reports. Corporate **27/27 / 258/258** stays exact.
Both selected original acceptance fixtures and **30/30 selected development
documents** are complete. The question-mark export `inv-126` now matches its
unchanged 185×180 transparent reference through all six APIs on Windows/Linux.
Six separate browser controls and 23 core checks protect bounded inert typography
on geometric leaves, including instances, pattern content and filtered capture.
The existing blur and native geometry rendering needed no additional correction.
Six earlier blur references still exercise whole playback through six APIs,
alongside 15 focused resource, opacity, ownership and boundary checks.
Arc-path directions, point-list vertices and their instances, units, CSS overflow, remaining presentation
combinations and finite effects remain promised.
G2–G9 and S17 remain open.

Static `paint-order` supports `normal`, distinct ordered fill/stroke/markers lists
and inherited/initial/unset values through the admitted cascade and instances.
Omitted operations append in default order; fill/stroke overlap uses that order
without changing native primitive paths. Local start/mid/end markers on admitted
lines and straight/quadratic/cubic paths use definition paint and bounded content.
Arc directions, point-list geometry and instance combinations remain G8 work.
Pattern-container presentation inherits through ordinary resource scopes. Non-unit
container opacity and inherited gradient-stop presentation remain explicit gates.
Validated stop color/opacity on geometry leaves does not affect their fill/stroke.

## Original-scope support audit (2026-10-10)

The following are current observed gates, checked against the executed bank,
recorded-paint controls, new online sources and parser code. Older implementation
checkpoint paragraphs below retain their dates. The ordered work and exit
criteria live in [PLAN](../PLAN.md#3-ordered-support-queue).

| Queue | Supported boundary / missing combination | Retained evidence |
| --- | --- | --- |
| G1 | Declaration validation landed: attributes and admitted inline/stylesheet paths share `new` / `accumulate` / `inherit` validation without backdrop allocation. Complete and numeric partial rectangles require finite numbers and nonnegative dimensions. | Eleven six-output contexts plus malformed/priority/resource/discovery controls; the unchanged `inv-105` is now complete after G3 rectangle support. |
| G2 | Ordered symbol leaves, nested groups/local uses and native-backed magnified instances render. Root and rendered-group locations, anonymous owners and descriptive metadata work. Solid use color/fill-opacity, numeric stroke-opacity and admitted stroke paint/width/mechanics inheritance bind in clones; group display/visibility propagate; reference graphs validate and expand iteratively. Unstyled closed unpainted paths retain centerlines for use strokes. Rendered groups replay authored styles/fill rules and local transforms through shared lowerers, including nested/forward uses and aliases; copied leaf/use masks preserve their batches. Instance shader fills and independent fill opacity now replay through ordinary lowerers; solid borders and admitted pattern phase/scaling survive mapping. Root/group/link/symbol shader fills inherit through the admitted cascade; fractional point-list shader fills retain native contours (patterns require untransformed placement). Lines/images ignore non-applicable fill paint. Root/nested viewBox extents now supply user-space percentage bases to ordinary use positions/sizes, default use sizes, nested viewports and admitted images; percentage roots resolve against the containing viewport or existing intrinsic defaults without a viewBox. Scaled nested viewport uses, other locations and affine combinations remain open. | Both original families exact on three backgrounds; all 46 preceding controls now exact. All sixteen retained-content controls and three diagnostic twins are exact after the viewport-opacity repair. The preceding symbol bank remains 83 exact comparisons through six outputs. The use-paint bank has 31 cases: 30 exact and one explicit rejection; thirty rectangle-path stroke controls are exact. The stroke-opacity bank has 70 exact and 11 explicit rejections. The stroke-style bank has 96 exact and three translucent shader-circle rejections; the rendered-group bank has 114 exact and nine explicit rejections. All 150 instance-shader/opacity controls and 192 container-shader controls are exact through six outputs. The 81 percentage-base controls retain 78 exact and three scaled-use rejections. |
| G3 | Plain transformed rectangle fills now use shared straight-contour lowering with native convex source identity; rotation/reflection and composed non-uniform placement are admitted, including inherited group clips and rendered-group aliases. Nonsingular general matrix fills and mapped endpoint arcs now use shared source-space conics. Scalar stroke maps require perpendicular equal-length columns; singular syntax, transformed compound leaf clips, rounded/stroked/pattern rectangles and non-uniform curve output retain separate gates. Unrounded affine linear/radial rectangle fills now share native rectangle source replay and source paint matrices; percentage rectangle coordinates use the known user-space viewport. Pattern reflection and non-uniform curve maps remain gated; the I Ching compound even-odd witness was already exact. Drawable paths retain their complete prefix before malformed syntax, and instance replay preserves native single/compound contour identity. | `inv-105` and `original-logo-rotated-mark` are exact; fifteen focused controls now retain eleven exact and four rejected outcomes. `inv-106` is now complete through six outputs on both platforms. The three `original-number-general-map` backgrounds and complete explicitly qualified tiger are exact. Remaining cases: `original-curve-root-quad-none`, `group-clip-reflected-pattern`, `iching-library/rotated-symbol-gradient` and the discovered `oval-source-replay/non-scaling-circle-tiled` width-map defect. The retained Malta, Portugal, Bhutan and South Africa flags are complete at nine frozen outputs each. South Africa uses shared native clip-content replay; its native/small/large defects improve 273/216/18 → zero on all backgrounds. Native fills retain cached source concavity across affine placement; twelve source-convexity controls are exact. Fifteen native fill/replay controls are exact. Integer orthogonal fills retain authored commands, source ovals share the native constructor, and thin strokes replay in bordered bitmap coordinates with native region-clip material routing. Fifteen new affine/density controls are 12 exact plus three explicit boundaries; native command count stays independent of duplicate-free classification geometry. `curved-path-clips/composed-group-cubic` remains explicitly unsupported after a measured nonuniform edge mismatch; Wikifoods is complete: nine exact outputs through six APIs on Windows/Linux. Clip construction retains the preceding enclosing rectangle, bitmap-owned border coverage and nonzero bounds. Radial drawing references compose before float conversion; both affine and axis-aligned inside focal paints use the shared native evaluator. The affine radial/clip bank has 23 controls: 22 exact and one explicit boundary; the older radial fill/stroke control graduates with the shared oval correction. Other nonuniform curve/clip and non-scaling width-map promises stay open. |
| G4 | Fractionally placed rectangle strokes preserve ordered hull/frame/inverse-fringe coverage; shared finite fractional widths now render the complete `inv-115`; a separate use-instance effective-width gate remains; the retained instance family is exact. Native solid strokes render; gradient/pattern mixed closed/open subpaths and gradient direct `fill="none"` lines still reject. Oval dash caps/paint/transform combinations remain narrower than solid paths. | `use-paint-properties/current-color-rectangle-stroke-*` and twelve new fractional/thin/collapsed/shader-fill frame controls are exact. Three new clip-crossing controls and the inline transformed twin reject separately. Nine `zero-area-stroke-*` paint-server controls, two `line-bounds-*-square-width6-fill-none` controls; shared stroke and paint admission. |
| G5 | Single user-space path clips retain original paints and native source replay, including concave/self-intersecting straight or curved contours and rectangle targets. Cancelling signed area does not discard nonempty local winding; collinear native contours clip to empty coverage. Independent nonzero/even-odd rules retain definition/ancestor inheritance and admitted inline/stylesheet priority. Target clip-rule and source fill-rule remain independent; legacy nonzero convex multi-contour intersection keeps its narrower gates. Compound curved paths retain all authored contours, holes and parity. Bounded disjoint convex straight clip children share one union source, with native cyclic starts, closure identity and contour order; overlaps, curved sibling unions, object-box ovals and larger multi-shape mask fallbacks remain open. Direct oval clips retain native oval identity. Definitions use child-local maps, and nested group scopes preserve outer-to-inner order. Arbitrary clip content retains a separate enclosing logical rectangle; bounded construction preserves bitmap origin and fully opaque bitmap intersections retain BW paint routing. Ordinary unscaled group clips are admitted; broader group/use combinations and coordinates/rules remain incomplete. Fractional symbol viewport clipping around isolated translucent groups renders exactly; composed affine symbol viewport rectangles now use native convex clip tapes and per-operation byte coverage, including admitted native contours and hairline strokes. Native symbol stroke instances remain exact. The retained nested affine-opacity defect is repaired by keeping instance opacity outside the instantiated viewport clip; broader viewport/use-opacity combinations need independent controls. Nested SVG style attributes, nested viewport paint definitions, scaled pattern viewports, percentage/custom transformed mask regions and deeper isolated opacity remain incomplete. | `symbol-content/group-alias-use` and its inline diagnostic are exact on unchanged references; eighteen fractional/integral, nested-scope and translucent-content controls are exact on three backgrounds. The three native symbol stroke backgrounds and their inline twin match unchanged references; thirty new rectangle-path stroke controls are exact. The I Ching bank has 23 exact and two rejected; seven new nested-affine-opacity twins are exact. The complete `inv-107` mask document is exact through six outputs on Windows/Linux. Root/defs single-leaf shader masks reuse bounded scene replay; default user-space regions and clipped-layer shader coordinates are measured. Twenty controls are 17 exact + three explicit rejections. Copied legacy mask curves retain their spans; shared native oval source replay completes `inv-094` at 9/9 exact frozen outputs. Eight oval controls retain seven exact and one measured non-scaling root-viewBox width-map defect under G3. General transformed spatial targets and placed custom regions, objectBoundingBox content, recursion and grouped mask content stay explicit boundaries in the finite exit. Ordered direct geometry/local-use children share bounded scene replay; the complete soccerball inv-114 is 1/1 exact with twelve separate exact controls. Authored mask-group markers now preserve deferred nested/sibling ownership, including empty groups, forward resources and independent source opacity: complete inv-117 is 1/1 exact with sixteen separate exact controls. Mask-root fill/color/alpha/rule and admitted inline/stylesheet priority reuse ordinary container inheritance. Shader-filled ovals retain independent solid/dashed strokes, and enclosed mask paint clips preserve native AA/BW dispatch through nested isolation: inv-118 is 1/1 exact with 33 separate exact controls. Non-unit mask-container opacity remains an explicit rejection; other G5 promises remain open. Earlier gates: `original-aa-clip-admitted-shader`, material pairs and `mask-region-half-translated`. Single userSpaceOnUse quadratic/cubic/arc clips replay native commands on ordinary leaves and unscaled groups, including uniform non-axis group maps. Ten curved controls, including the old concave control, and the older group clip control are exact; objectBoundingBox and nonuniform composed curve clips retain explicit boundaries. The complete warning sign is 1/1 exact. The complete gradient-mask export inv-113 is 1/1 exact with nineteen exact local-reference controls; single resource uses, nested mask definitions and axis mask placement reuse bounded source replay. Eighteen new winding/rule controls are sixteen exact plus two explicit boundaries; multi-contour parity also retains a categorized core boundary. |
| G6 | Ordered direct vector tile leaves, local templates, fractional grids and root-local definitions use bounded source replay. General local pattern maps admit rotation/reflection/shear; inherited solid target strokes and native rectangular stroke borders remain independent. Pattern image sampling reuses gradient bitmap-local coordinates with separate output/local matrices. Legacy unit-scale rectangles retain their route. General referencing-shape/instance maps, groups/uses inside content, content/viewBox/objectBoundingBox units and nested viewports remain open. Missing/cyclic/external templates and unsupported units retain explicit outcomes. | Vector-pattern example, PatternA, striped pattern and set-screw diagram are complete through six APIs on Windows/Linux; striped pattern is 9/9, set screw 1/1. Vector content: 17 exact + one rejection; inheritance/grid: 15 exact + two rejections; prior map/registration controls: 18/18 exact; general placement: 15/15 exact, including three CPU bitmap-origin witnesses. The original two-child cubic-pattern fixture remains exact. |
| G7 | Bounded inline/presentation typography on geometric leaves is validated and discarded without requesting fonts. Thirteen inline properties and nine SVG attributes share finite keyword/scalar syntax; text, containers, images and stylesheet typography retain separate functional rules. Embedded PNG and static CSS have explicit narrow subsets; non-uniform image content/resampling, root-targeting stylesheet selectors and additional export combinations need measured slices. | `SvgImageSyntax`, PNG decoder, `SvgStyleSheet`; source distinct export failures before expanding. |
| G8 | Local start/end line markers and start/mid/end straight/quadratic/cubic path markers support authored vertices, subpath roles, tangent bisectors, repeated/closed vertices, strokeWidth/userSpaceOnUse, numeric fixed/auto/start-reversed orientation, ref points, finite dimensions and viewBox/aspect mapping. Ordered vector/group/use content reuses bounded source replay. Definition paint stays independent of the target; element opacity surrounds fill/stroke/markers in typed paint order. Allocation bounds retain a separate layer role from masks. Hidden marker viewports preserve native binary clipping; visible overflow attributes render through existing source replay. XML shorthand is inert; admitted CSS shorthand and longhand declarations follow ordinary cascade. Local line/path instances propagate inherited/authored marker channels through retained leaves, symbols/groups and aliases, preserving resource paint, placed clips and isolated opacity. Arc-path directions, polyline/polygon vertices and their instances, units, CSS visible overflow, remaining presentation and remaining finite filters stay open. Local filter urls on live geometry retain ordered, bounded resource graphs and native SourceGraphic/SourceAlpha capture. Gaussian blur admits identity axes and device deviations 2 ≤ sigma < 135; integer offsets and black drop shadows reuse premultiplied source-over and authored filter regions. Colored sRGB blur is qualified; colored linearRGB arithmetic, fractional offsets, general affine filter maps, containers/instances, local clip/mask/marker combinations, primitive subregions and remaining finite graph nodes keep explicit current gates. Missing targets, recursive retained content and ordinary use of marker definitions reject explicitly; negative sizes are malformed. | Complete CC0 inv-121/inv-122/inv-123/inv-124 match frozen outputs through six APIs on Windows/Linux. The earlier 20 controls are 16 exact / four boundaries; 23 path controls are 20 exact / three boundaries. Both sets have healthy matching platform capture triples. `original-logo-marker-mark` is complete with 12 exact / three boundary controls and matching healthy platform triples; `original-logo-filter-mark` and `inv-125` are complete at frozen 64×64 / 128×128 transparent outputs through six APIs on Windows/Linux. Six frozen full-playback blur controls and 15 ownership/presentation/boundary checks protect this correction; `inv-126` is complete at frozen 185×180 transparent. Six exact inert-typography controls and 23 core checks protect its shared admission correction. |
| G9 | Direct text requires a prepared supplied-font snapshot. Public static Noto Sans is admitted through bounded ordered pair-list/class kerning, with covered contexts and actual marks explicitly rejected. The frozen original live-text output is complete through six raw/six prepared APIs on Windows/Linux. Linux retains six exact controls; Windows's seven controls, composites and broader text/tspan/text-path layout remain pending. Twenty-two axial TrueType geometry checks and the selected Windows A's 18 points/raw RGB mask are independently qualified; bounded managed LCD coverage and platform preblend compute from supplied font bytes. | `original-logo-live-text` complete; `original-logo-text-path` next; PLAN section 6. |

The retained CC0 Wikifoods logo is complete: all nine frozen original-input
outputs in [its bank](../tests/Research/wikifoods-logo/manifest.json) are exact through
six APIs on Windows/Linux.
Percentage circle/ellipse attributes now resolve against known user-space viewport
extents; circle radii use the normalized diagonal, ellipse radii their respective
axes. Native float length resolution is shared by geometry and paint bounds.
The [focused bank](../tests/Research/oval-percentage-lengths/manifest.json) has
nine exact controls and three explicit unsupported boundaries. Percentage oval
definitions remain gated pending host viewport retention in instance replay;
rotated ellipse maps and rectangle percentages keep their separate gates. Every
Wikifoods output is exact. The [affine paint/clip bank](../tests/Research/affine-radial-paints/manifest.json)
has 22 exact controls and one explicit geometry boundary; its former radial fill/stroke rejection now matches the unchanged reference. Shared paint
coordinates, bitmap-owned clip bounds and native source replay complete the logo.

The CC0 striped pattern (`inv-109`) is complete: nine frozen outputs in
[the remaining-scope bank](../tests/Research/remaining-scope-documents/manifest.json)
are exact through six APIs on Windows/Linux. Root-local pattern registration,
inert aspect declarations, composed positive-axis pattern maps and bounded tile
replay share existing mechanisms. The [placement bank](../tests/Research/pattern-affine-placement/manifest.json)
now retains eighteen exact controls; the reflected/rotated cases graduate against
unchanged references. Development completion is now 22/22 selected documents.

The non-ionizing radiation warning (`inv-110`, G5) is complete: its frozen
616×540 transparent output is exact through six APIs on Windows/Linux. Native
directed winding and bounded clip/content replay retain the bowtie's two lobes.
The [winding bank](../tests/Research/native-clip-winding/manifest.json) protects
sixteen exact controls plus two explicit boundaries; clip-rule precedence repairs
five measured 126-pixel errors, and the old concave-curve control becomes exact.
The geometric pattern (`inv-111`, G3/G5) is complete: its unchanged 284×284
transparent output is exact through six APIs on Windows/Linux. Deferred ordinary
leaves retain source command order, inherited attributes/scopes and pending ranges
until local paints resolve. Non-axis linear path fills retain source endpoints,
pre-placement object bounds and separate native local/reference matrices. Genuine
rotation/skew controls are exact, alongside reflected placements. The
[29 controls](../tests/Research/deferred-paint-leaves/manifest.json) retain 26 exact
results and three explicit missing/cyclic/partial-mask boundaries. The partial-mask
gate and other finite group promises stay open; no broader text admission is claimed.
Every declared input, option, matching browser frame and reference stays unchanged;
healthy platform capture triples are reused.

The five retained online failures have original bytes, separate root-sized render inputs
and stable browser references in [the inventory manifest](../tests/References/inventory/svg-gap-cases.json).
They are failure-selected development cases, outside the 27-document corporate
cohort and original fixture/research denominators. No new renderer behavior is
claimed by recording a rejection. First-blocker removal may expose further
admission or pixel failures; all remain visible until the unchanged reference is exact.
The complete Illustrator heart, 79-instance I Ching library, both patterns and mask
document are exact on Windows/Linux through six outputs (**5/5 complete**), with
source/options/reference unchanged. Each has a matching fresh Linux browser
triple. The retained 64×64 nested affine-opacity control is exact too. The radiation
symbol is complete through all six APIs: 9/9 frozen outputs exact on Windows/Linux,
with every old input, option and reference unchanged. Eighteen development
deliverables are complete, including the explicitly qualified tiger, all nine
Malta/Portugal/Bhutan/South Africa/Wikifoods/striped-pattern outputs each and the
warning sign's complete 616×540 output, geometric pattern's 284×284 output,
set-screw's 512×538, gradient-mask example's 512×880 and soccerball's 195×195 outputs. The
tiger's historical auto-image PNG remains diagnostic. All eleven straight-clip
controls, including the group boundary, are exact through six APIs on both
platforms. Healthy Linux triples match each frozen document/control reference.
Both platforms pass 5,931 tests after the soccerball correction. Complete-bank
qualification protects all preceding exact comparisons and the unchanged SVG/PNG
assets/options. Corporate remains 27/27 / 258/258 exact on own-platform references.
At this soccerball-mask checkpoint the selected queue was 18/18 complete. The
following four retained exports now bring it to 22/22; new sourcing targets the
remaining finite support groups.
Controls remain separate.

The [new export bank](../tests/Research/remaining-gap-exports/manifest.json) keeps
three complete fontless static exports and matching healthy, cleaned platform
triples. `inv-112` is now exact at its unchanged explicit 512×538 transparent
output. General pattern grids, native rectangular stroke borders, inherited
target strokes and shared bitmap-local image coordinates complete its rotated
vector hatch. Fifteen [new placement controls](../tests/Research/pattern-general-placement/manifest.json)
protect phase, alpha, fractional output, inherited content, shear, reflection,
curved targets and nonzero software bitmap origins.
`inv-113` is complete at its unchanged 512×880 transparent output through six APIs
on Windows/Linux. Single local uses in masks and single-contour user-space clips
reuse geometry lowering and bounded native scene replay, including forward
references. Nested clip/linear/radial definitions in masks share document scope.
Nonsingular axis mask placement composes with output coordinates. Source-bound
rectangle/path fills admit general local linear paint maps; arbitrary-stop ramps
use native float factors/biases, and stop offsets reuse native float parsing.
Clip envelopes round in source coordinates before placement. Mask shader
matrices and dithering share each bitmap's actual clipped layer origin. Nineteen
[local-reference controls](../tests/Research/mask-local-references/manifest.json)
are exact against matching healthy, cleaned platform triples.
Placed geometry references carrying general linear paint maps, general painted
strokes/point lists, transformed compound clips, and placed spatial targets with
custom/fractional mask regions retain explicit UnsupportedFeature gates. Missing,
recursive, grouped-content and objectBoundingBox resource combinations stay
bounded and explicit. Other nested-definition locations remain open.
`inv-114` is complete at its unchanged 195×195 transparent output through six APIs
on Windows/Linux. Direct ordinary geometry/local-use mask children replay in order
after definitions resolve, sharing existing source lowering and vector-scene ownership.
Luminance conversion follows composition of the complete scene. Existing single-child
routes, gradient targets and masked groups preserve their native distinctions.
Twelve [ordered-mask controls](../tests/Research/mask-content-order/manifest.json)
are exact separately from the whole document; the old multiple-child boundary
also becomes exact against its unchanged PNG. Grouped content, first-child legacy
color/visibility restrictions, wider unit/region and composition promises stay open. This correction closes no finite G2–G9 exit. General referencing-shape and
instance maps, groups/uses inside tile content, viewBox/content/bounding-box units
and nested viewport promises remain open.
Remaining G2 combinations are dependencies only when the
selected document needs them. Finite planned G2–G9 exits live in PLAN; they do
not change the currently admitted subsets recorded here.

Shared background declaration validation charges text work before span-only
tokenization and retains no backdrop or token-array owners. Active filter effects
remain categorized. The case manifest now records the heart's complete exact
outcome; its original and reference remain frozen. See [the repair](RASTERIZATION_DESIGN.md#shared-exporter-background-validation-2026-10-08).

## Symbols and instances (2026-10-08)

Symbols directly inside admitted `defs`, the root or rendered groups retain zero
or more supported self-closing geometry leaves, nested groups and local uses in document order. Empty symbols paint nothing;
multiple children share one owned definition. Distinct child IDs are aliases,
while duplicate authored IDs remain malformed. Tested aliases exclude the
enclosing symbol's viewBox, fill and opacity; numeric stroke opacity binds independently
for aliases, as do admitted inherited stroke paint/width/mechanics. Definition ancestors do not supply transforms, visibility
or opacity to copies. Symbol IDs are optional; anonymous definitions retain
child-ID owners through scene construction. Group/use ID aliases borrow typed
source ranges and exclude referenced ancestors. Container display/visibility and
independent solid-fill properties resolve through the instance context. Other
definition locations and broader instance styling remain gaps.

Title/description/metadata subtrees are non-rendering under admitted containers,
including groups and links, and cannot register child geometry IDs. Their
non-active descendants neither validate drawable attributes nor request fonts;
later eligible text still participates in discovery. Active SVG content still rejects; all nodes, attributes, XML depth
and parser work remain bounded. Lowering and font discovery share symbol
location and late-stylesheet checks. Discovery retains later admitted text
requests without requesting fonts from symbol/metadata content. Five existing
provider/lowering scenarios and two fresh denial/retry checks cover that boundary.

Symbol presentation attributes and admitted inline/stylesheet declarations
resolve fill/color/fill-opacity/fill-rule, basic inherited stroke and container
opacity. Authored solid fills win over a use fill; unstyled fill channels inherit
from use and its ancestors. Color and fill opacity inherit independently of fill:
`currentColor` resolves in each clone, explicit leaf/symbol values win, and child
aliases exclude the symbol's values. A leaf's own display/visibility cannot be
reactivated by inherited fill. Geometry and recorded alpha share that resolution.
Literal, rgba and currentColor solid stroke markers retain independently authored
or inherited numeric stroke opacity and bind at instance time. Admitted stroke
paint/width, caps, joins, miter limits and dashes inherit independently; authored
source values win. Existing shader strokes retain their
separate byte-alpha boundary. Child, symbol and use opacity
compose in separate scopes. Nested group visibility/display now propagates in
clones. Solid use color/fill-opacity attributes now follow the admitted cascade,
including forward targets, nested uses and aliases. CurrentColor stroke paint
binds independently; retained native symbol stroke cases now match unchanged references.
Rendered-group use styles share the same authored/inherited precedence and local
transform replay. Local linear/radial/pattern use fills and numeric fill opacity
now resolve through the same source lowerers, retaining authored solid/none/shader
fill precedence, independent solid borders and currentColor. Root/group/link/symbol
paint-server fills now inherit through the same cascade and replay. Use viewport
defaults and magnified uniform curve maps have exact independent controls;
non-uniform/scaled-nested-viewport and other existing consumer gates remain.

Closed axis-aligned rectangle paths use the shared native verb recognizer before
stroking. Explicit closure selects simple miter/bevel/round outer contours with an
opposite inner rectangle when it fits. Redundant monotone and zero lines are legal;
open rectangles, curves and multiple contours retain generic joins/caps. Recognition
uses construction-space floats, before final instance placement. All thirty new
controls and the retained three native stroke backgrounds are exact.

Native-backed curves replay retained source commands at the final instance map.
Definition circle/ellipse fills retain original float conics for that replay;
flattened outlines without an authored tape retain the magnification rejection.
Both original families are exact on three unchanged references each, and sixteen
independent controls are exact through six outputs. Denied allocation/work and
partial assembly cancellation preserve cleanup and retry. All fourteen additional
location controls are exact, including the three former nested-content
rejections. Sixteen further solid-fill presentation controls repair ten preceding
pixel failures while preserving six already exact controls. All are exact through
six outputs; four new exhaustive ownership inputs protect recolored native
geometry and partial failures. See [the initial repair](RASTERIZATION_DESIGN.md#ordered-symbol-children-and-native-instance-replay-2026-10-08)
and [the presentation follow-up](RASTERIZATION_DESIGN.md#symbol-solid-fill-presentation-at-instance-time-2026-10-08).
Sixteen retained-content controls and three diagnostic twins are exact against
unchanged references after the shared viewport-opacity repair. Eighteen new
controls bring the complete symbol bank to 83 exact six-output comparisons.
The [content slice](RASTERIZATION_DESIGN.md#retained-symbol-groups-and-local-reference-graphs-2026-10-08)
records iterative graph validation/expansion; the [viewport follow-up](RASTERIZATION_DESIGN.md#isolated-opacity-under-symbol-viewport-clips-2026-10-08)
records the repaired clipping/layer arithmetic. The [use-paint follow-up](RASTERIZATION_DESIGN.md#use-paint-properties-and-source-stroke-identity-2026-10-08)
records the new solid paint bindings and measured stroke/paint-server/group limits.
The [frame repair](RASTERIZATION_DESIGN.md#ordered-rectangle-stroke-frames-2026-10-08)
records the now-exact rectangle strokes and remaining clip-crossing gate.
The [stroke-opacity follow-up](RASTERIZATION_DESIGN.md#stroke-opacity-at-instance-time-2026-10-08)
records the original opacity repair. That bank now has 70 exact cases and eleven
retained gates: nine fractional diagonal lines and two late stylesheet inputs.
The three shader rectangle definitions and rendered-group styles now match
their unchanged references. Unstyled closed unpainted paths preserve their authored
commands for newly added solid use strokes. Numeric opacity syntax follows the
existing parser; the opacity slice did not broaden CSS keyword or stroke-mechanics admission.
The [stroke-style follow-up](RASTERIZATION_DESIGN.md#instance-stroke-paint-width-and-mechanics-2026-10-08)
records immutable source attributes/cascaded style, replay through shared lowerers,
the 96 new exact cases and the old inherited-symbol-stroke graduation. The three
translucent shader-circle cases retain the existing G4 gate. Plain rectangle
non-miter joins and wider instance/viewport combinations remain open under G2/G4/G5.
The [rendered-group follow-up](RASTERIZATION_DESIGN.md#rendered-group-styles-and-native-source-replay-2026-10-08)
records shared source replay, CSS/fill-rule/local-transform precedence and
leaf/use mask preservation. Its 123 controls now have 114 exact and nine rejections:
three group opacity, three group clip and three transformed source rectangles.
The three inherited shader-fill-opacity cases now match unchanged references.
Broader container mask/viewport, text and image copies retain gates. Native solid oval strokes at hairline widths
use the retained centerline route; nine new thin-oval comparisons are exact.
The four older group paint/opacity cases are exact without asset changes.
The [instance-shader follow-up](RASTERIZATION_DESIGN.md#instance-shader-fills-opacity-and-pattern-placement-2026-10-08)
adds 150 exact six-output controls and graduates eight older failures without
source/reference changes. Native paths/circles/rounded rectangles share source
lowering; shader rectangles retain independently painted solid borders. Pattern
instances preserve phase and positive-axis scaling with integral device tile sizes,
including fractional mapped content edges and bounded bilinear repeat sampling.
The [container-shader follow-up](RASTERIZATION_DESIGN.md#container-shader-inheritance-and-tile-paint-2026-10-08)
adds 192 exact six-output controls covering container URL fills, authored/CSS
precedence, aliases, native shape fills and isolated opacity. Fractional point-list
fills now admit linear/radial paints and untransformed patterns; other pattern
point-list placements reject explicitly. Direct pattern rectangles retain their
group map, while tile fill opacity survives byte quantization. Fifteen resource
sweeps and two retained placement gates pass. Broader pattern tiles/maps and
viewport combinations keep G2/G5/G6 open.
The [percentage-base repair](RASTERIZATION_DESIGN.md#viewport-percentage-bases-2026-10-08)
adds 81 six-output controls: 78 exact and three scaled nested viewport use
rejections. Root/nested viewBox sizes supply percentage bases independently of
output pixels; ordinary uses, omitted use dimensions and admitted images share
those bases. Root percentages without viewBox resolve against the containing
viewport or existing intrinsic defaults. Unknown bases without root dimensions,
viewBox or an explicit viewport retain their gate. Discovery uses the same extents
and keeps later font requests after nested percentage content. Eight resource
sweeps and four focused checks pass. The old viewBox-only/omitted-nested-size
rejection is a positive regression preserving its original geometry.

## Retained renderer foundations

These implementation notes retain their checkpoint measurements. Current
outcomes and remaining work are governed by the status and support audit above.

Butt-capped solid-paint circle/ellipse dashes now measure and cut original float
oval conics through the shared native dash/stroke route. Directed generated
outlines and filtered centerlines retain curves through final mapping; zero-radius
ovals paint nothing. Offsets, seams, thin widths, root scaling, non-scaling strokes,
pathLength and rectangle clips have independent exact browser controls. Existing
non-uniform/reflected placement, round/square dash caps, paint-server dash and
magnified-use admission restrictions remain in G2–G4 of the support queue.
See [the repair record](RASTERIZATION_DESIGN.md#source-conic-ellipse-dashes-2026-10-08).

Admitted integer diagonal polygon/polyline fills retain original source commands
for directed raster coverage across solid, linear, radial and pattern paints.
Normalized loops still own scene bounds and admission; open polyline fills close
implicitly while their source tape preserves the open ending for stroke semantics.
The retained integer bowtie is exact against its original browser PNG. Existing
multi-crossing even-odd and reflected-pattern-group admission gates stay explicit.
See [the repair record](RASTERIZATION_DESIGN.md#integer-point-list-source-replay-2026-10-08).

Mask/run dispatch uses complete outward-rounded vertex/control bounds before
canvas, bitmap or SVG clipping. Canvas-clamped row storage, bitmap scan windows
and paint clips retain their separate roles. This closes the fourteen retained
control-bound comparisons and the clipped star stroke across the shared four-paint
boundary. Eight resource sweeps preserve bounded setup/storage, denial cleanup
and caller-token cancellation. Other stroke and fractional-event differences
remain open. See [the repair record](RASTERIZATION_DESIGN.md#whole-path-blitter-dispatch-bounds-2026-10-08).

Convex polynomial quadratics now retain live curve state in both unclipped
and clipped construction. This preserves native strip-boundary advancement and
smoothing through the shared solid, linear, radial and pattern paint boundary.
The quadratic solid-fill and linear/radial painted-outline research comparisons
are exact against their original references. The fractional quadratic clip is
exact after the group clip placement repair below; these exact examples do not
establish general clip parity.
Failed paint-filled stroke construction/repainting releases geometry owners and
their containers. Six full parser-work-denial regressions cover solid, linear,
radial and pattern combinations. See [the repair record](RASTERIZATION_DESIGN.md#live-convex-polynomial-quadratics-2026-10-08).

User-space rectangle clips on groups use the complete inherited axis-aligned
group map, then the unscaled viewport offset, before intersecting the parent clip.
Forward references retain that map at group entry. Later child transforms leave
the group's clip in its own reference space. Both retained fractional cubic and
quadratic clip comparisons are exact. Translated, scaled and reflected controls
cover the shared solid/linear/radial/pattern boundary; reflected pattern paint
still rejects. General affine group clips and leaf-specific clip composition
remain inputs to the original-scope support audit. See [the repair](RASTERIZATION_DESIGN.md#group-rectangle-clip-placement-2026-10-08).

Uniform group masks apply once to composed children through an isolated surface,
independently of backward/forward definition order. Overlapping children, rounded
fills/strokes, patterns and alpha/gray values have exact independent references
on three backgrounds. Fully white uniform masks retain their resolved default
regions and source drawing state; nested group opacity retains its rejection. Adjacent groups preserve distinct mask
values through command insertion and reference expansion; validated empty groups
produce no scope. Leaf fill/stroke batches and masked use instances now compose
before uniform mask application; copied masks retain sharing within one instance
and distinct identity between instances. Backward masks with forward clips on
direct leaves have exact independent controls. The retained circle/ellipse alpha
masks and both curved-opacity controls are exact after bitmap-local curve setup.
The scaled masked circle is exact after preserving direct fringe paint outside
path-row storage. Hard mask-region edges round
in output coordinates
with half-pixel ties toward positive infinity. Content and mask bounds both
contribute to the isolated layer's dither origin; the mask clip stays separate.
All four polygon paints construct curves in the owning isolated bitmap before
conic subdivision and edge setup. Drawable polygon stroke bounds stay separate
from gradient coordinate boxes.
Accumulated analytic coverage remains path-bounded; emitted direct paints retain
the bitmap/SVG clip and expand output traversal only when necessary. Link storage
grows under memory/work accounting and cancellation. This closes three retained
wide-ellipse comparisons without changing any previously exact bank result.
Independent hard mask-region group/leaf/copy/scaled-root controls are exact. Transformed custom
group masks retain a valid-input rejection. Selected
default mask regions use object bounds without strokes and hard region clipping;
source visual bounds survive lowering and tested use/viewport placement. Wider
affine/custom-region combinations and copied forward clips remain unproved. The
retained multiple-child symbol now renders exactly; wider symbol combinations
remain in G2.

Source paint values and logical draw identities remain separate through lowering
and instance copies. Element opacity retains its float factor until the selected
solid, shader or isolated layer stage; CSS shader fill/stroke opacity retains its
own byte quantization. One-stop rectangle shaders preserve raw stop alpha and
bitmap-local dithering. The direct, parent/leaf and instance-override controls are
exact. Degenerate multi-stop rectangle shaders retain their authored final stop
through constant-color packing and skip dithering; six fractional coverage and
destination-overlap controls are exact. Other solid-lowered routes retain their
existing behavior and measured precision gaps.

The [recorded-paint controls](../tests/Research/paint-recording/manifest.json)
retain 94 exact results and seven valid-input
rejections. Translucent paint-stroke combinations newly admitted by deferred
opacity have independent browser evidence; this does not establish all stroke,
pattern, mask or override combinations.
Rounded rectangle fills and strokes retain shared native float source commands,
with rational conic corners and native rectangle/oval simplification. Validated
source shapes retain their known convexity through preparation and placement,
including half-side radii with zero-length straight edges. Independent fractional,
clamped, reflected and painted controls are exact. SVG source command order starts
at the top tangent or an oval's top midpoint; the retained hairline-color and
oval-stroke research cases are exact. Other curve/stroke/clip differences retain
their separate measured gates.

Painted integer paths with diagonal edges retain directed authored contours,
including touching/coincident subpaths and even-odd crossings. Linear/radial
gradient and pattern paths retain whole fill contours and scene paint boxes.
Axis-aligned authored fill paint boxes use original float curve extrema before
scene mapping, including polynomial and rational curves and endpoint arcs.
General affine, legacy and stroked geometry retain their existing box route.
Direct solid orthogonal paths keep checked integer bands. Closed definitions
eligible for instance-supplied strokes preserve authored paths instead, so a
`<use fill="none" stroke="...">` keeps its stroke with integer source coordinates.
The existing unstyled-source and definition-transform stroke gates remain.
All four independent integer-path leaf/parent-opacity controls are exact.
Authored straight replay uses
command-bounded scratch; rectangle mask and additive-run boundary bytes differ.
Original independent controls remain exact; broader fidelity is still open. Native
filled-path rectangles recognize original line verbs, including monotone collinear
pieces and repeated points. Eligible clipped boxes use float fringe coverage and
direct painting; thin or declined boxes retain quarter-snapped edges. Mask/run
eligibility uses whole-path bounds; AA paint clips keep their existing forced-run
route. The rectangle shortcut allocates no edge/row scratch.
Authored quadratic fills retain their controls when fallback flattening emits
one chord, across single/multiple contours and both fill rules. Existing stroke,
clipped preparation and legacy flattened-curve admission remain unchanged.

Authored fills retain source contours once for bitmap-local mapping. Float
translation moves into the bitmap before source point/control mapping; clipped
edge construction and vertical combination stay local, then kept fixed-point
walk/curve positions translate together. Raw-control raster bounds retain
extrema missed by flattened samples. Solid, linear, radial and pattern fills
share this preparation. Quadratic recurrence starts use snapped Y; clipped
rational curves enter bitmap coordinates before subdivision. The original native
legacy expression and finite-output fallback match observed browser circle
coverage; its browser build define remains unconfirmed. Broader shape,
transform, stroke and paint fidelity remains open.

Declaration-only DOCTYPE admission applies to bare, SYSTEM and PUBLIC declarations
before the root. External identifiers are inert; internal subsets (even empty),
duplicates and malformed syntax remain `MalformedInput`. No DTD resources,
declared entities or defaults are loaded. Original byte encodings and structural
limits are preserved across root parsing, font discovery and scale probing.

Marker longhands and CSS shorthand admit local references through the finite G8
line/path/instance slice above; the XML shorthand attribute remains inert.
Shared validation also admits default `enable-background:accumulate`; the root
attribute retains its validated `new` hint. Backdrop filtering remains unsupported.
`overflow:visible` attributes admit marker content and non-viewport targets;
marker CSS visible overflow and root/nested SVG visible overflow stay explicit
boundaries, including matched stylesheet declarations.

The complete frozen corporate matrix has 258 rendered comparisons: 258 exact,
zero known differences and zero renderer rejections: **27/27 documents**,
marks **13/13**, wordmarks **14/14**. All nine Zeppelin variants are exact after
retaining generated source stroke outlines until the composed final map. Every
earlier exact image and categorized rejection remains. Original inputs, options
and browser references stay frozen. Fractional filled polygons and polylines retain
source vertices and closure beside scene admission and paint geometry; bounded
command owners replay through the composed final map. Rectangle operands narrow
to native floats after original-value validation; source endpoints are added at
float precision before scene placement. IBM is exact in all nine variants,
completing the full frozen-cohort target. S17's numerical minimum is also met.
Representative Windows performance, local Windows/Linux delivery and the full
Linux browser matrix are validated. Remote CI remains open.

Isolated root/group opacity uses native image-layer source-over, summing scaled
source and destination lanes before the final byte shift. Uniform masks retain
their factor separately and multiply the isolated surface before outer opacity,
including forward references. Original white-background opacity controls are
exact; the curved-layer control retains one differing pixel. Spatial masks,
region gates and nested-opacity restrictions keep their existing scope.

Concentric general radial paints retain separate shader-local and referencing
matrices. The drawing CTM moves into bitmap coordinates before local-gradient
concatenation, inversion and unit-circle mapping. Retained parser/scene paint
storage is reserved before allocation and transferred through clipping/masks.
The existing focal/conical fallback remains. Native numeric vectors and six
independent browser controls verify this order; broader paint fidelity remains
open.

Eligible authored fills preserve directed winding and retained curves without
scene contact surgery. Root/group placement composes before float rounding;
scene bounds and paint coordinates remain. SVG endpoint arcs retain native
float rational conics; wider path arcs construct native quadratic offset outlines.
Eligible solid/dashed path hairlines retain conics. General event, coordinate
and clip/paint fidelity remain open.

Solid path strokes retain centerlines, closure and width maps. Dashed paths use
bounded native curve measurement and cuts, with retained conic weights, zero ON
cap dots and closed-seam continuation. Twelve new thin dash controls are exact;
six wider controls keep their known differences. Transformed/pathLength dash
browser coverage remains open.
Paths up to one device pixel replay lines, quadratics, cubics and endpoint-arc
conics through native fixed-point hairline coverage, ordered paints and
butt/square/round cap extension. Twelve new thin curves match their stable native
references; six wider controls retain their known differences. At that checkpoint,
every previous fixture/research image and all 258 corporate outcomes/PNGs were preserved.
Generic wider paths and integer/fractional polylines share native curve offsets,
joins/caps, closure and directed retained outlines, including dashes and painted
instances. Generated outline commands retain source operands and directed contours
before scene normalization; their owned storage and instance copies are bounded
and released with the centerline. Single and coincident round-dot paths can have
different native pixels; independent controls preserve that distinction.
Bare movetos paint nothing; non-scaling dashes keep host-coordinate lengths.
Full stroke/paint/clip fidelity and other hairline contexts remain open.

Circle/ellipse strokes fit normal offsets of original conics with retained
quadratic verbs, including an inverted inner offset for very wide strokes.
The existing positive uniform-scale and ellipse-rotation admission limits remain.
Native-stroke quadratic coverage consumes retained controls even when fallback
flattening has one chord. Linear/radial/pattern `use` copies preserve and reserve
curve spans. All twelve fresh oval controls and all nine SG Rotation variants
improve but remain inexact; all prior exact PNGs and dispositions are preserved.
Remaining generic stroke scan/paint residuals and sampled shape routes remain open.

Straight solid open strokes retain native float normals, endpoint miter replacement
and pivot inner joins through directed nonzero winding. Intersecting rings cannot
select convex fill. Bevel joins are admitted on polylines through the same kernel
as paths, including direct/inline/inherited styles and existing dash construction.
Circle/ellipse fills and native straight solid open-stroke round caps, dots and
joins retain rational verbs through final float mapping. Device-space subdivision
reuses quad clipping; convex walking advances live state with native smoothing.
Native nonzero stroke contacts retain signed walking, including reversals and
self-touches; intersecting rings cannot select convex fill. Four original probes
and three fresh reversed-join configurations become exact. All 837 prior exact
fixture PNGs and 70 exact corporate PNGs are preserved. Every Rede Manchete
variant improves at that checkpoint. The width-one cap probe then worsened
15 → 16 differing pixels; the subsequent hairline repair makes it exact against
the same original reference. Broader fractional events, curved/dashed overlap and clip/composition fidelity
remain open. Ordinary fill/contact gates retain their scope.
Bitmap-local edge clipping and float root mapping preserve all nine exact Artsy
variants.

Straight dash endpoints now retain native float interpolation. Nonzero orthogonal
contour sets without retained spans use signed-winding analytic walking through
crossings and coincident edges, preserving the one-byte ROC fringe previously
lost in geometric fallback. Classification is cached and charged once. All nine
ROC variants and twelve fresh controls are exact; every prior exact PNG is
preserved. Curves, self-touching contours and coincident even-odd sets retain their
separate gates. General stroke fidelity remains open.

Actual rectangles retain original fill edges and separate paint clips through
viewport, affine, symbol and instance mappings. Solid materials use round/255
premultiplication before rounded native 24.8 shape coverage. Gradient/radial/
pattern bands preserve the same clip, including pattern content and solid stroke
bands. The 960 native pixel vectors and 36 new browser configurations establish
thirty exact cases and six known shader quantization differences. The original
material grid and translucent border band become exact; all 669 previous exact
PNG identities are preserved. All nine IBM variants improve but remain inexact.
Successive clip-alpha intersections, cache history and general paint fidelity
remain open.

Rectangle and polygon gradient/pattern samples retain premultiplied float lanes
through coverage and source-over, with one final byte conversion. Gradient stops
retain source stop-opacity through clamping, style and href resolution while CSS
color alpha keeps its byte encoding. The browser distinguishes `stop-opacity=".5"`
from `rgba(...,.5)`; all six fresh alpha comparisons are exact. Eighteen existing
fixtures and one research probe graduate with their original images. Twenty-four
fresh configurations add twenty-one exact and three known notched-pattern cases.
All 735 prior exact fixture PNGs and 66 exact corporate PNGs are preserved;
27 SimpleLogin/SDL/Liputan 6 variants improve but remain inexact. The corporate
rate stays 3/27. Gradient coordinates/interpolation, notched edges, successive
clip/cache behavior and general shader fidelity remain open.

Gradient dither cells now use the owning software bitmap's local indices while
paint samples stay in canvas space. Original material-pair controls become exact;
large linear/radial probes improve to one/four differences. Twelve fresh tiled
configurations add nine exact controls and three radial cases with four RGB-only
differences each. All 775 prior exact PNGs remain unchanged. Six intended/large
SDL variants improve; corporate exact documents remain 3/27. Coordinate
construction, remaining geometry/clip events and full cache behavior remain open.

Linear and centered radial coordinates now use native float unit matrices,
bitmap-local seeds and affine inversion. Gradient-transform numbers share the
bounded float reader, work counter and cancellation owner; legacy RGB and
gradient paint opacity normalize before fused interpolation. Seven original
configurations graduate, with the research ellipse transferred using its original
assets. Twelve fresh configurations add nine exact controls and three geometry
differences. All 787 previous exact PNGs and all 258 corporate outcomes are
preserved. All nine SDL variants improve; corporate exact documents stay 3/27.
Conical/multistop sampling, wider mapping, fractional geometry/clip events and
full cache history retain separate gates.

Convex rectangular strips round coverage separately from the raw-rectangle
calibration. Native event evidence closes YouTube's opaque triangle witness;
thirteen fresh configurations become exact and one shader control retains known
paint differences. Rectangle clip bytes now close the remaining YouTube boundary
differences: all nine variants are exact against their original references.
Twelve new nonrectangular path configurations are exact across horizontal,
vertical, corner, opacity and nearly integral clips. Integer orthogonal `path`,
`polygon` and filled `polyline` now retain their contour and separate AA paint
clip instead of clipping rectangle bands during lowering. All twelve original
path-band controls and eighteen new scaled box/notch configurations are exact.
All 628 prior exact fixture PNGs were unchanged at that checkpoint. The newer
rectangle repair above retains paint clips across actual `rect` and paint-server
bands; general clip stacks and shader quantization remain partial.

Single clean retained contours now distinguish raw control-polygon convexity
from sampled-ring convexity. Float sign/turn/reversal classification is charged
once and cached across raster contexts, preserving genuine convex routes and
declined-hint fallback. The 75 native vectors match; twelve independent controls
and one existing fractional shoulder become exact. All nine Ford variants retain
exact PNG identity. Ford and YouTube completed both selected checkpoints;
at that checkpoint the corporate matrix had 240 fidelity comparisons and four
verified exact documents. All eighteen then-rejected variants had independent references.
General mapped curve/paint events remain open.

Rectangle paint-clip classification is independent of construction clipping and
path containment. Native float nearly-integral/edge-snapping rules select binary
or antialiased paint contexts; the latter retain additive run storage and ordinary
fringe-pair forwarding across solid, linear/radial-gradient and pattern paints.
Per-boundary provenance preserves geometric origins through intersections,
reflections and reference placement; mask allocation regions contribute no AA
paint edge. The 188 native predicate/setup vectors and 41 independent browser
configurations retain twenty-one exact, eleven known-difference and nine existing
UnsupportedFeature outcomes. This bounded proof does not cover complete native
AA masks, successive clip-alpha intersections or all cache histories. General
clip-byte composition and curve/layer pixels remain partial.

Analytic coverage reserves complete live-edge objects, list growth and retained
curve expansion before allocation. Cubic forward-difference positions evolve
independently of walked X, with a separate displacement preserving the current
quarter-grid pixels; fractional endpoint fidelity remains implementation work.

Eligible native full-canvas solid rectangles preserve source fill-opacity for
the pinned browser's bounded float paint-analysis route. Paths, generated bands,
paint servers, opacity layers and scenes beyond its five-draw limit keep their
existing raster routes; opaque recipe-3 page backgrounds consume two draws.
The 80 new routing configurations include 79 exact controls and one retained
group-opacity-white difference. That bounded rectangle repair establishes no
general alpha/layer fidelity; Ford's later paint progress is recorded below.

Eligible analytic fills now preserve ordered direct single/pair/span paints
separately from pending additive runs, with the native bounds-selected mask/run
route and operation-specific uniform/shader arithmetic. All 12 original dispatch
controls are exact. A further 24 material configurations bank 15 exact and nine
known differences. Float capture conversion and compact full-span storage retain
bounded ownership/work and Default admission. At the paint checkpoint, Ford
improved in all nine variants but remained inexact; circle/stroke residuals include
mixed alpha-error changes.

Run walking now uses a scan window distinct from path storage/dispatch when the
complete raw path and retained controls fit inside the clip. The mask route keeps
its path window. Eighteen independent phase/scale/clip/material configurations are
exact; five research controls graduate with unchanged references. At that checkpoint all
nine Ford PNGs were unchanged. The later clip-built construction below preserves
that boundary; general clipping remains open.

Retained cubics now enter the existing live/pre-stepped paths even when their
flattening has only one chord; quadratic gates stay separate. Every cubic setup
attempt, including zero-height pieces, is charged within existing complete-span
reservations. Twelve independent phase/scale/material configurations retain eight
exact and four known differences. All prior fixture/research PNGs are unchanged;
six small/intended Ford variants improve and the three large PNGs are unchanged.
At that checkpoint intended-transparent retained 433 differing pixels; general cubic event fidelity
and the all-nine wordmark exit remain open. Default inventory renderability is
preserved; ten changed non-Ford outputs lack matched oracle evidence.

Shared clipped-edge preparation now uses the independently verified line/quad/cubic
kernel when complete path/control bounds escape the canvas or explicit clip.
It clips every retained eligible verb, preserving X/Y monotonic splitting,
projected winding and empty pieces. Two charged stack-scratch passes size and
construct complete edge storage under existing limits. All four paints share the
boundary; fractional clip coverage remains per operation. Mask walking intersects
construction bounds with path storage, while run walking uses construction bounds.
Existing contact/retention gates remain; this does not admit new syntax.

At the clipped-preparation checkpoint, all six small/intended Ford variants became
exact; large variants improved but retained 4,900/4,956/4,404 changed pixels.
All nine original references and current candidate
identities are portable regressions. All 493 prior exact fixture PNGs remain exact.
One predeclared clip configuration and the subsequent fractional clipped-stroke
witness are exact; the star-stroke witness remains a known difference. Twenty-six
clip configurations retain failures, including the quadratic phase's increased
alpha error. The updated bank retains both directions and every rejection.

A traced follow-up records one CPU bitmap tile for small/intended Ford and eight
for large Ford. Bounded CPU raster contexts now carry bordered construction clips
and exclusively owned interiors through all four paints. Whole-path mask/run
dispatch stays separate from bitmap-bounded row storage, and the converter closes
the original row-6 alpha witness. The six exact Ford PNGs retain their identities;
at that checkpoint large transparent/white/black variants improved to
929/1,245/560 changed pixels,
with transparent total alpha error 3,573. The all-nine wordmark exit remains open.
Sixteen context controls retain four exact results, eleven known differences and
one UnsupportedFeature rejection. Twenty-nine changed Default inventory outputs
lack matching captures in that sweep; their fidelity is unmeasured. Actual native
authoring, tile-local mapping and full-image paint parity remain work in PLAN.

Horizontal clip wrappers now forward direct fringe pairs as ordered ordinary
runs. Complete path bounds include retained controls; vertical-only clipping and
contained paths preserve specialized pairs. All fifteen solid configurations of
the 18 predeclared controls are exact, while three shaders retain differences.
Seven fixture failures and one research probe graduate with unchanged references.
The six exact Ford PNGs are preserved; large differences fall to 174/174/159 and
transparent alpha error to 2,835. Two quadratic clip controls gain three alpha-error
units each; 26 changed Default inventory outputs lack matching captures in that
sweep. Those failures and evidence gaps remain explicit. General curve/event,
convex/quad/stroke, clip/mask and layer fidelity remains open.

Path numbers and transform arguments now share independently verified float
reading; relative/smooth path updates use float lanes, while general affine
computation keeps doubles and its existing gates. Thirty-one browser numeric
vectors, all 436 captured Ford points and the actual large CTM are reproduced.
Proper crossings in separated multi-contour even-odd fills keep their directed
contours and zero signed-area lobes. Exact terminal axis adjacency preserves raw
points and closes an `inv-040` admission regression; single-ring split/admission
and other contact gates remain. All 12 parity/retrace controls are exact; the full
30-configuration bank also retains 12 known differences and six rejections.
All 660 previous fixture PNGs and every existing render/reject disposition are
preserved. Ford keeps six exact variants and improves large variants to 10/10/7,
with transparent alpha error 16 at that checkpoint. The density repair below
now closes the all-nine Ford exit; general corporate fidelity remains open.

Intersection-density dispatch now uses the raw points represented by admitted
retained curve verbs, with ordered-range validation, charged cancellation-aware
traversal and a borrowed per-fill cache. Declined hints preserve flattened counts;
clipping does not change whole-path point count. All nine original Ford variants
are exact, preserving all six prior exact PNGs. The independent 24-configuration
bank retains 15 exact controls, three known differences and six categorized
rejections. No syntax gates or resource limits expand. General affine/instance,
fractional events, quad/convex/stroke and layer fidelity remain explicit limits.

R36 line dashes also admit presentation attributes, inline styles and stylesheets on the line, with group and root inheritance rendering identically to direct attributes.

Stroke widths flow through the same cascade: presentation attributes, inline styles and matching stylesheets on the shape plus group and root inheritance render identically to direct attributes, with negative widths still rejected at lowering (zero widths paint nothing for solid and paint-server strokes while positive fractional widths lower through the shared outline kernel; line/rect-band and paint-server band strokes keep integer discipline). A width with no stroke paint anywhere renders fill only on rectangles and circles. Stroke paint inherits through group and root containers to shapes without their own (explicit none blocks it); paint-server strokes keep their dedicated rejection except at zero width, where nothing paints and the fill renders alone; text with painted strokes stays unsupported before font acquisition. Miter limits flow through the same cascade with sub-one values still rejected at lowering. Stroke opacity multiplies stroke alpha on solid strokes while paint-server strokes stay opaque-only. Path lengths flow through the same cascade with the single lowering-time validation unchanged. Stroke linecaps and joins flow through the same cascade with per-shape lowering enforcement unchanged and invalid values still rejected at parse.

## Legacy delivery overview

The table below retains the original delivery foundation and earlier admission
restrictions. Later shared renderer work described above supersedes those
restrictions; this table does not define current syntax admission. The planned
scope audit will reconcile it against the implementation and failing SVG examples.

| Area | Historical foundation and limitations |
| --- | --- |
| Public options, limits and failure categories | Initial contract and categorized public failures implemented; current rectangle raster work is preflight-bounded; polygon fills charge mapped vertices up front with setup and coverage by taken traversal (analytic rows cost pixels, fan keeps pixels-times-points). Lowering and font-request discovery both reserve the greater of 16 KiB, four times input bytes, or 512 bytes per configured XML-attribute limit before `XmlReader` construction; it is an accounted renderer allowance, not a process-RSS cap or exact BCL allocation measure |
| Root SVG parsing | Bounded validation counts every element including the root against `MaximumNodes` (matching font-request discovery) and rejects XML nesting beyond the configured `MaximumXmlDepth` (128 by default) or more than `MaximumAttributesPerElement` attributes (256 by default) before this parser consumes them; inert `id`, title/description/metadata and unused definitions (container-wrapped definition content is explicitly unsupported rather than painted); empty foreign-namespace editor elements are ignored while non-empty foreign content stays explicitly unsupported; self-closing rectangle, supported axis-aligned-line, or polygon/polyline/path/circle/ellipse fill definitions in `id`-only-or-attribute-free direct or group-nested `defs` (link-nested `defs` stays explicitly unsupported), or local `symbol` definitions directly under defs/root/rendered groups (with optional ID, `viewBox`/`preserveAspectRatio`, containing zero or more such self-closing direct geometries) can be instantiated by direct or group/link-nested local `use`/`xlink:href`, including forward references, with definition-local geometry (definition ancestor transforms do not move instances; transformed definition attributes apply exactly once); group-targeted uses replay typed source leaves/groups/local uses through shared lowerers, preserving authored styles and local transforms without importing source ancestors (admitted curves, rectangle strokes and paint servers no longer depend on un-baking scene geometry; group opacity/clip/mask/viewport boundaries and text/image content stay explicitly unsupported); `switch` renders its first child without a `systemLanguage` attribute, evaluating selection against an empty locale set, `x`/`y` placement, bare-number, percentage or omitted-`100%` `width`/`height` (omitted dimensions and percentages require known parent dimensions), bare-number or percentage `x`/`y` placement, integral `x`/`y` line offsets, the documented finite transform subset (`translate()`/`scale()`/`rotate()`/`skewX()`/`skewY()` plus axis-aligned `matrix()`, per-consumer axis gates below), and use `opacity`/`display`/`visibility` via presentation attributes, inline styles or generic stylesheet selectors (`id`/`class` are admitted for that purpose and capped at 1,024 characters). Root `display`/`visibility` propagates to each supported direct or nested local `use` instance as well as rendered leaves. A `fill` supplied on a `use` instance inherits into source channels without authored fill; independently authored solid, none and shader fills win. Local linear/radial/pattern use fills and numeric fill-opacity replay through ordinary/forward leaves, symbols, rendered groups, nested uses and aliases under the ordinary shape/paint admission gates. Source precedence, independent solid borders and currentColor follow the contract above; userSpace vectors compose instance/symbol placement, bounding-box vectors follow their existing transform rules, and reflected pattern placements remain unsupported. Symbol instances map content through the symbol `viewBox` into the use viewport (all `meet`/`slice`/`none` alignments, defaulting `xMidYMid meet`), then apply instance placement/transforms and clip to the instance-transformed viewport box; lines map via stroke bands. Direct local `use` instantiates polygon/polyline/path/circle/ellipse fills (including multi-geometry orthogonal bands and fill-plus-stroke pairs, backward and forward) with the same placement/transform/opacity/clip composition; definition-embedded linear-gradient bands instance per band through the same vector and opacity mapping, including through symbol viewBox maps, and definition-embedded pattern bands instance per band through the shared tile and opacity mapping for direct references and through admitted positive-axis symbol viewBox maps with integral device tile dimensions, fractional mapped content edges and integer/fractional/negative repeat phase; fractional tile dimensions and reflected/general pattern maps remain unsupported; symbol-held polygon fills (including fill-plus-stroke pairs) and orthogonal bands instance through uniform viewBox maps with the same composition, while non-uniform symbol polygon maps stay explicitly unsupported; masked polygon fill/stroke batches compose inside isolated leaf/instance scopes with shared contour storage; forward multi-geometry references expand in place preserving paint order. Direct scalar presentation/layout values and inline-style property/value tokens cap at 256 characters, and transform values at 4,096; inheritable `color`, `fill`/`fill-opacity`, `display` (`inline`/`none`) and `visibility` (`visible`/`hidden`) on root and `g`/`a`; isolated root or one group `opacity`; the same supported properties through narrowly parsed inline `style` declarations, with inline precedence; bare-number/px/in/cm/mm/pt/pc dimensions implemented at 96 CSS pixels/inch; root-targeting stylesheet selectors, references to other geometry, non-`id` definition-container attributes or child content, remaining symbol content/style combinations described above, remaining geometry/style root and other group attributes and relative units explicit unsupported; non-rendering `role`/`tabindex`/`focusable`/`pointer-events`/`cursor`/`aria-*`/`data-*`, link `target`/`rel`/`hreflang`/`download`/`type`/`title`, legacy root `version`/`baseProfile` and foreign-namespace editor attributes are ignored; outermost root `x`/`y` (validated bare-number/px, without effect), root `xml:space` (validated `default`/`preserve`, whitespace-mode wiring pending) and root `enable-background` (validated `new`/`accumulate`, no backdrop compositing) are admitted without rendering effect; root `class` is admitted, inert unless a stylesheet class rule matches it (root-targeting rules stay explicitly unsupported, enforced in both the renderer and font discovery) |
| Viewport resolution | An explicit host `SvgViewport` overrides the root outer dimensions. Root `viewBox` mapping is implemented with all `meet`/`slice` alignments or `preserveAspectRatio="none"`, only when every supported rectangle edge maps to an integral or quarter-pixel output edge; other sizing and fractional-coverage cases explicit unsupported. Nested `svg` viewports with explicit bare-number or percentage `x`/`y`/`width`/`height` (percentages against known bases, including percentage-root bases in parent user units through the threaded explicit viewport with viewBox sizing and default-intrinsic fallback; omitted dimensions under dimensionless roots stay explicitly unsupported) shift admitted non-text content by the viewport origin and clip it to the viewport box (intersected through nesting, stroked rectangles admitted only fully inside); nested `viewBox` mappings scale content through the shared alignment helper with per-feature gates (strokes, pattern fills, use instances, clip paths and custom-region masks stay explicitly unsupported in scaled viewports) while viewport percentage dimensions, viewport styling and viewport text stay explicit unsupported (nested viewports isolate one presentation-attribute `opacity` scope; nested scopes stay explicitly unsupported); hidden `overflow` is accepted on root and nested viewports |
| Empty explicitly sized SVG | Integral transparent or caller-background PNG implemented |
| Caller-owned PNG destination | Synchronous/asynchronous `Memory<byte>` and writable nonseekable `Stream` destinations implemented for the current empty/rectangle subset; span-visible input/output overlap is rejected before parsing/writing, including custom memory providers; streams stay open and may be partial on failure |
| Integer rectangles | Document-order layers, bare-number/px integer coordinates, ordered lists of finite `translate()`, nonzero `scale()` (including reflections), `rotate()` with optional center, `skewX()`/`skewY()` and nonzero axis-aligned `matrix(a 0 0 d e f)` transforms on containers (rectangles, lines, true ellipses, arcs, gradient/pattern paints and clips keep the axis-aligned subset; general `matrix()` forms stay explicitly unsupported), and output-scaled geometry (only when scale preserves integral edges), SVG zero defaults for omitted coordinates/dimensions and zero `rx`/`ry`, the standard CSS named colors plus `none`/`transparent`/`currentColor`, comma or CSS Color 4 space/slash-form `rgb()`/`rgba()`/`hsl()`/`hsla()`/`#RGB`/`#RGBA`/`#RRGGBB`/`#RRGGBBAA` fill, with `fill-opacity` multiplying any color alpha, `nonzero`/`evenodd` fill rule, non-painting `stroke="none"`, and a separately bounded stroke case: untransformed integer rectangles with solid or translucent color and even integer `stroke-width` no larger than either dimension (`stroke-linecap` butt, `stroke-linejoin` miter, `stroke-dasharray` none and finite `stroke-dashoffset` accepted as no-ops), instanced through direct local `use` with placement, uniform-scale widths, multiplied opacity and viewport containment (shape definitions resolve from defs or as direct root children, which also render directly), and through `symbol` viewBox maps as fill plus four viewport-clipped stroke bands with uniform-scale even widths and integer-mapped geometry, while non-uniform scales, non-uniform symbol maps and fractionally-mapped symbol strokes stay explicitly unsupported; element `opacity` is exact only for fill-less strokes, and the corresponding supported inline properties, source-over; other stroke styles and attributes explicit unsupported |
| Fractional rectangle coverage | Analytic per-pixel area coverage for integer or quarter-pixel decimal/px rectangle edges (including only mappings that preserve quarter-pixel device edges), with a calibrated fractional source-over branch; exact pinned-Chrome overlap probe admitted. Other fractional edges, fractional paths, and fractional point-list coordinates remain unsupported |
| Shapes, paths and composition | Simple closed, absolute, axis-aligned integer `polygon` elements, filled `polyline` elements (the fill closes the contour), and `path` elements using `M`/`H`/`V`/`L` commands in either case with implicit repeated coordinates and relative movetos resolved from each open subpath end (closepath returns to the subpath start) (segment commands after closepath resume at the subpath start in both grammars; dangling segments stay malformed) with sign-abutted integer coordinates split into separate numbers lower to bounded non-overlapping rectangle bands (redundant collinear vertices merged exactly; `Z` may be lowercase), including transverse-intersecting multi-subpath contours with `nonzero`/`evenodd` fill rules (evenodd pairs crossings, nonzero sweeps winding per crossing pair; coincident vertical edges resolve through evenodd pair cancellation and nonzero winding accumulation (abutting contours, retracing, spikes and touching holes lower to exact disjoint bands; degenerate zero-width pairs and odd leftovers stay explicitly unsupported), every unclosed subpath seals implicitly for fill and lowers identically to the closed form, as does an explicit return to the contour start without closepath, while closed single-point figures stay explicitly unsupported; subpaths with fewer than three distinct points seal to zero area (empty fill) while solid strokes outline every subpath, lowering fully-degenerate open centerlines to cap-determined dots (flatness-bounded discs for round caps, axis-aligned squares for square caps, nothing for butt caps) with zero-length dash runs discarded; the same drop rule applies to flattened curve and fractional device-space all-degenerate fills, while mixed degenerate and non-degenerate contours stay explicitly unsupported there and under paint-server strokes, masks and clips; solid diagonal `polygon`/`polyline` fills, integer- or fractional-coordinate straight `path` fills, and fractional-coordinate `polygon`/`polyline` point lists (device-mapped exactly like fractional paths, including strokes) with separated simple contours lower to shared polygon outlines with analytic winding/evenodd coverage (opposite-wound nonzero holes and nested even-odd holes included; nonzero/evenodd fill rules inherit through root and group containers with leaf values winning and other values rejected as unsupported; linear-gradient fills evaluate on the same outlines through paint-box geometry) while touching, coincident and multi-contour self-intersecting diagonals stay explicitly rejected at the lowering gate pending general coverage (single self-crossings split into simple loops sharing the crossing point, with bowtie unions pinned; nonzero multi-crossing contours split iteratively under the pairwise loop guard with even-odd multi-crossing staying gated); integer-valued (including integral decimal/exponent spellings) quadratic/cubic/smooth `path` fills flatten through the same outlines at quarter-pixel flatness with arcs admitted only under uniform-positive transforms, while non-integral coordinates, degenerate curves and curve fills under non-uniform output scaling stay explicitly rejected; solid `circle`/`ellipse` elements and nonzero-`rx`/`ry` rectangles lower through sampled ellipse and rounded-rectangle outlines with the same coverage (integral-scale viewBox maps on solid rounded rectangles byte-identical to direct rendering), with linear- and radial-gradient fills on both repainted through shared paint-box evaluation (pattern-tile fills on rounded rectangles and circles); solid `circle`/`ellipse` strokes lower to opposite-wound sampled ring fills with measured oracle delta (positive integer or fractional widths, validated-only miterlimit, uniform scales; round and square caps with round joins and bevel render byte-identical to butt/miter on the smooth rings; butt-capped dashes measure/cut original float oval conics through the shared native dash/stroke route, with dash offsets and pathLength normalization flowing through, and definition-held and symbol-held dashed ellipses rendering byte-identical to direct play), as do linear-, radial- and pattern-tile paints on circle and ellipse rings through white ring lowering with shared repaint (opaque-only; translucent and dashed pairs staying explicitly rejected; paint-stroke definitions and stroked symbol instances byte-identical to direct rendering), while non-circle clip content stays explicitly rejected; solid positive-integer-width `polygon`/`polyline`/`path` strokes (open butt, round or square caps, miter, round or bevel joins on paths and polylines with per-subpath explicit-closure tracking, miter limits with bevel fallback, bare-number/px dash arrays with at most eight entries and odd-doubling plus finite dash offsets, bare-number path lengths scaling the pattern by computed over asserted length (zero pinning a solid stroke), fill-plus-stroke in paint order) lower to shared nonzero outline fills with fractional-capable output mapping (closed loops merge seam-touching runs preserving joins; all-zero patterns paint no stroke; zero-length dashes need butt caps; userSpaceOnUse linear-gradient strokes repaint straight polygon/polyline outlines with zero added paint error, as do bounding-box gradients on straight polygon/polyline outlines and straight path outlines through fill-bounds paint evaluation, as do bounding-box gradients on curved and fractional paths through fill-geometry-bounds evaluation, as do linear, radial and pattern paints on fractional point-list outlines through the same shared machinery, userSpace gradients on curved path outlines, and userSpace radial gradients on straight outlines and curved paths, and pattern tiles on straight outlines and curved paths through tile-grid evaluation with zero added paint error, and linear-, radial- and pattern-tile outlines compose through direct use instances with contour and paint placement mapping (uniform instance scales only; native-backed curves replay under uniform magnification; flattened outlines without retained source commands stay explicitly unsupported), and linear-, radial- and pattern-tile outlines through symbol viewBox maps with uniform-scale gating (uniform instance scales only; native-backed symbol curves replay under uniform magnification; flattened outlines without retained source commands stay explicitly unsupported), while triangle outline probes render with measured oracle delta of 186 pixels at maximum channel error 15 and maximum alpha error 9 on any gradient paint and a half-tile pattern triangle renders 88 pixels inside that standing set at the same error bounds and a curved arch renders with measured delta of 254 pixels at maximum channel error 255 and maximum alpha error 107 from shared flattening), while dashed rectangle band strokes and fractional line/rect-band widths stay explicitly unsupported, as do dashed circles and ellipses outside butt caps and uniform scales; attribute, inline and stylesheet dash lists all evaluate through the admitted dash kernel on lines, polygons, paths and circles, as do non-uniformly scaled strokes except through `vector-effect="non-scaling-stroke"` on polygon/polyline/path/circle/ellipse, which keeps the width fixed through element transforms including non-uniform ones (dashes keep scaling normally; non-uniform dashed strokes, band strokes and stylesheet/inline/inherited vector effects stay explicitly unsupported); rectangle, gradient and symbol-held rectangle band strokes keep miter joins with butt-only caps on dashed strokes, while round and square caps on solid closed strokes lower as butt (spec no-ops proven byte-identical across direct, gradient-band and symbol paths), with solid rounded-rectangle strokes retaining conic source verbs and separate fill/source-outline owners under the same caps, as do linear-, radial- and pattern-tile paints on rounded-rectangle outlines through white-solid lowering with shared repaint (opaque-only with stop and tile alpha gates; translucent, dashed and bevel pairs staying explicitly unsupported, with round and bevel joins admitted on rounded outlines while sharp-rectangle round and bevel joins stay gated), with definition-held rounded rectangles lowering through the same shared path for direct use instances with paint-stroke definitions, symbol instances and white-masked strokes (all byte-identical to direct rendering)). Untransformed horizontal or vertical `line` elements with solid even-width butt or square strokes and multiplied element/stroke opacity (square caps extending tips by half the width across solid, gradient and pattern paints, round caps lowering opaque solid strokes to a band plus two sampled tip discs, as do linear-, radial- and pattern-tile paints on round-capped lines through white band-plus-discs lowering with shared repaint (opaque-only with stop and tile alpha gates), solid butt-capped lines also admit dash arrays with offsets as dash-run bands, as do linear-gradient, radial-gradient and pattern-tile paints on dash-run bands through the shared run computation with single-stop and degenerate solids, with pathLength normalization scaling dash patterns by computed over asserted length across solid and paint-server dashes (zero pinning a solid line, negative rejected as malformed), square caps extending each dash run by half the width through the same shared run computation (opaque nonzero dashes only; translucent and zero-length square dashes staying explicitly unsupported), and their integral local `use` instances through the same axis-aligned transform subset with integer line offsets and integer effective widths, retain axis-aligned line geometry before lowering to a stroke band (userSpace gradient strokes evaluate on the band, including through integral local `use` placement, while bounding-box gradient strokes stay unsupported). One `g`/`a` opacity scope is isolated in a bounded temporary RGBA surface before source-over composition; nested opacity scopes are explicit unsupported pending separate calibration. Coincident-edge configurations (shared subpath edges, backtracking spikes, collinear overlaps), non-integral curve coordinates, all other path grammar and shapes explicit unsupported-feature failure. Grayscale single-rectangle `mask` definitions with content covering the masked geometry, plus single-element solid-grayscale circle content evaluated spatially on masked groups through a temporary mask surface (single paint; multi-element, chromatic, multi-paint, non-polygon, leaf/use-targeted, text-bearing and transformed-viewport content stays explicitly unsupported) (id plus `luminance`/`alpha` `mask-type`) modulate admitted leaves and `use` instances through `mask="url(#id)"` (quoted forms accepted; forward mask definitions admitted on use and symbol instances, on direct leaves (deferred values with default, userSpaceOnUse and bounding-box regions resolving against retained bounds) and on direct groups (rewritten or inserted isolation scopes with retained coverage), viewports still requiring backward references, default regions, explicit userSpaceOnUse regions admitted on direct leaves and use/symbol instances outside nested viewports, stroked rectangles admitted only fully inside and masked groups composite on isolated surfaces with separate opacity and mask operations and custom (explicit or bounding-box-fraction) regions clipped at composition with clip-box edge discipline (region-only scopes composite without opacity interaction; nested group opacity inside opacity scopes stays unsupported; each masked-group child, including bands, polygon fills, stroked extents and deferred uses, requires covering mask content and text inside masked groups stays explicitly rejected; empty masked groups carry no scope) while masked text stays explicitly rejected; chromatic luminance content, mask styling and non-covering or transformed mask content stay explicitly unsupported while explicit userSpaceOnUse mask regions clip direct leaves and explicit bounding-box regions resolve per referencing box on direct leaves and use instances with specification defaults and content-coverage policy (other units and group targets stay unsupported). On direct leaves and use instances, single-value masks stamp linear-gradient, radial-gradient and pattern-tile outline strokes like polygon fills (white-mask pass-through proven byte-exact on gradient outlines); non-white masks on overlapping fill-plus-stroke paint evaluate per geometry rather than per element, translucent-mask dither order is uncalibrated, and stroke-fringe mask-region edges are uncalibrated. General masks remain unsupported. Single-rectangle and single-straight-path `clipPath` definitions resolving from defs or as direct root children (id-only with optional nonzero/evenodd `clip-rule`; `clip-rule` is also admitted but unwired on regular shapes, groups and roots, userSpaceOnUse or objectBoundingBox units; rectangular paths lower to boxes while single convex straight contours in userSpaceOnUse intersect leaf polygon fills and paint polygons at document end with element transforms composed at retention, and multi-contour convex unions concatenate per-pair intersections (exact for nonzero fills including translucent and gradient paints; even-odd needs pairwise-disjoint contours with vertex touching, overlapping contours explicitly unsupported), and concave, curved, degenerate, transformed-ancestor, transformed-viewport, group, group-use-instance and non-polygon-geometry targets stay explicitly unsupported) clip `g`/`a` containers, admitted leaves (including paths, lines and gradient bands) and local `use`/symbol instances through `clip-path="url(#id)"` (quoted forms accepted; forward clip-path definitions admitted on use and symbol instances and on direct shape leaves in userSpaceOnUse (rectangles, rounded rectangles, bands, straight contours and embedded images) or objectBoundingBox (rectangles, rounded rectangles, bands and straight contours), and on direct groups in userSpaceOnUse through deferred command ranges with passthrough viewport frames, other targets still require backward references); objectBoundingBox fractions resolve per referencing rectangle, band, straight-contour polygon fill, flattened curved contours, fractional point lists or direct use instance box (backward and forward use references), with symbol instances resolving through the viewBox-mapped content box (backward and forward use references), while non-integral curves, forward clip-path definitions outside use, symbol instances and direct leaves (including bounding-box forwards on lines, circles, images and groups, masked or scaled groups, and any forwards on viewports), and other targets stay explicitly rejected, transformed fraction content stays unsupported while transformed userSpace clip content composes through the admitted axis-aligned transform subset (rotated clip content stays explicitly unsupported); nested clips intersect, stroked rectangles admitted only fully inside, clipped text explicitly rejected; clip boxes map through the root viewBox and output transform with the clipped geometry, and fractional clip edges scale per-pixel coverage exactly like fractional rectangle edges (byte-exact against the pinned oracle on the solid-polygon probe) |
| CSS, gradients, images, filters and text | Attribute-free root-level `style` elements, each preceding every supported definition and rendered leaf, may contain ordered comma-separated `rect`, `g`, `#id`, `.class`, `rect.class`, `rect#id`, `g.class`, `g#id`, `[id]`, `[class]`, `[id=value]`, `[class=value]`, `g rect`, and `g > rect` selector rules using the narrowly documented declaration subset. `rect`/`g` type selectors and group combinators retain their stated scope; generic ID, class and id/class-attribute selectors apply to every currently supported shape/container carrying those attributes, including paths, point lists, lines and `use` instances; supported identifier tokens cap at 1,024 characters. Group paint/display/visibility then inherit to descendants. Per-property `!important` priority, source order and selector specificity (`#id` > class/attribute > type selectors) are implemented. Inline important declarations override stylesheet important ones; stylesheet important declarations override normal inline ones; normal inline declarations override normal stylesheet rules; stylesheet rules override presentation attributes. `inherit`/`initial`/`unset` keywords resolve for the paint subset (`inherit` where the property inherits, static initial values otherwise; unplumbed `inherit` stays explicitly unsupported). `font-variant: normal` (or `none`) and `color-interpolation="sRGB"` are accepted as no-ops. Integer-grid `userSpaceOnUse` single-rectangle `pattern` definitions tile admitted rectangle and orthogonal path/point-list band fills through `fill="url(#id)"` (quoted forms accepted; explicit grid/content integers, content inside the tile (shifted content and stroke extents included), solid content paint, no nested patterns or definition reuse; fill-paired strokes stay solid while pattern-url strokes on rectangles and lines evaluate the shared tile grid on stroke bands (even widths, butt caps, unscaled viewports, definition and line composition, pairing with solid and gradient fills); pattern fills pair with gradient-evaluated stroke bands through fill-plus-band geometries, as do pattern fills with pattern strokes through shared tile evaluation; integer translate-only pattern transforms shift content; tile content admits clip paths in either unit system, element opacity, positive integer content scales and in-bounds solid strokes while masks on content, tile-escaping shifts and tile-overflowing strokes stay explicitly rejected); other pattern units, fractional grids, overflowing content, reflected placements stay explicitly unsupported; pattern fills apply to rectangles, orthogonal path/point-list bands and diagonal polygon/path outlines repainted with the tile, with curved paths flattened through shared device outlines carrying a small curve-edge residual, while other targets stay explicitly unsupported; pattern fills under root viewBox or output scaling map their integer tile grid through the device transform while fractional tile maps and stroked tile content under scaling stay explicitly unsupported; tile content raster work is charged per blitted pixel with bounded cancellation checks; curved pattern fills pair with admitted solid stroke outlines through flattened geometry with the standing curve-edge delta; orthogonal and diagonal pattern fills pair with admitted solid stroke outlines in paint order; fill opacity composes onto pattern fills while solid strokes keep element opacity only. General selectors/cascade, stylesheet attributes, stylesheets outside the direct root position, scripts, animation and stylesheets after styleable content stay explicitly unsupported. Filters are unsupported. One-to-eight-stop linear gradients in objectBoundingBox or userSpaceOnUse units (stops carry alpha interpolated under the calibrated straight-ramp premultiplied rule, non-decreasing offsets for multi-stop gradients (falling offsets rise to the previous stop, forming hard edges with the later stop winning at the shared offset per the pinned oracle; zero clamped ranges stay unsupported), clamped into [0,1] and retained per gradient with one scene-owned reservation per distinct stop array (shared href and band references counted once), pad/repeat/reflect spread, no non-pad degenerate vectors (degenerate pad-spread fills render the end-stop color, as do single-stop gradients); userSpaceOnUse vectors map through the referencing transform and reject percentage coordinates, with transformed viewports composed through the viewport map and non-uniform referencing transforms evaluated through the recorded inverse paint map; gradientTransform composes finite translate, positive scale and positive axis-aligned matrix forms eagerly into the vector and rejects rotated, skewed, non-positive and non-finite forms; objectBoundingBox vectors mirror with reflected referencing geometry transforms; solid fills pair with gradient-evaluated stroke bands under the solid-stroke geometric policy, and gradient fills pair with gradient-evaluated stroke bands through fill-plus-band geometries, with pattern fills pairing with gradient-evaluated bands the same way; single-stop gradient strokes render their stop color on the bands, as do degenerate pad-spread strokes with the end-stop color; gradient-stroke rectangle and line definitions instance through the same placement and vector mapping, including through symbol viewBox maps; backward href inheritance merges inherited stops with whole-vector or same-space partial-vector overrides under explicit mixed-space gates; radial gradients inherit stops and circles through the same same-type href mechanism in both `href` and `xlink:href` spellings while linear-to-radial references stay unsupported) fill admitted rectangles (gradient definitions resolve from defs or as direct root children), diagonal linear- and radial-gradient polygon and path outlines and orthogonal path/point-list bands with sRGB-direct transposed-Bayer dithered interpolation, with linear-gradient fills on fractional-coordinate straight and curved paths evaluated on shared device outlines repainted through the same paint-box geometry evaluated in normalized bounding-box space; one-to-eight-stop radial gradients in objectBoundingBox or userSpaceOnUse units (focal point defaulting to the center and admitted strictly inside the circle through point evaluation, contained focal circles defaulting to zero radius through two-circle cone evaluation with negative radii rejected as malformed, pad/repeat/reflect spread on the circle parameter, finite translate, positive scale forms (uniform, or non-uniform for userSpace circles through recorded inverse-map evaluation with pre-transform focal validation) and positive axis-aligned matrix gradient transforms composed eagerly into the circle; userSpace circles map with their focal point and radius through the referencing transform, with non-uniform scales and transformed viewports evaluated through the recorded inverse paint map, reject percentage coordinates) fill admitted rectangles, diagonal polygon outlines, curved paths flattened through shared device outlines with the standing curve-edge delta, and orthogonal path/point-list bands through normalized-space evaluation with the same stop interpolation, single-stop solids and degenerate end-stop rendering, and compose through direct use instances (bounding-box and userSpace placement with paint-server fills), symbols (viewBox scaling with recorded inverse-map scales, uniform for contours) and single-value masks exactly like linear fills; solid fills pair with radial-evaluated stroke bands under the solid-stroke geometric policy on butt-capped rectangles (even widths, unscaled viewports) and userSpaceOnUse lines, and radial fills pair with radial-evaluated and pattern-evaluated stroke bands through fill-plus-band geometries, while non-pad degenerate radial strokes and bounding-box line strokes stay unsupported, while on-or-outside-circle foci, escaping focal circles, rotated, skewed, non-positive and non-finite gradient transforms, non-uniform transforms on bounding-box circles, viewport-nested maps and percentage userSpace coordinates stay explicitly unsupported; over-eight-stop and non-pad-degenerate linear gradients stay explicitly unsupported, as do rotated, skewed, non-positive, non-finite and empty linear gradient transforms. Gradient-filled curved paths with solid strokes compose through flattened outlines with the standing curve-edge delta; radial- and pattern-filled curved paths with strokes and wider paint-server fill-plus-stroke combinations retain their shape-specific gates; shader rectangle instances preserve admitted independently painted solid borders; orthogonal and diagonal linear and radial fills pair with admitted solid stroke outlines in paint order; fill opacity composes onto gradient fills while solid strokes keep element opacity only; group-inherited gradient fills stay unsupported. Solid-color base64 PNG data-URI `image` elements with explicit bare-number `x`/`y`/`width`/`height` (or omitted dimensions defaulting to 100%) lower to exact rectangles with CRC-checked chunk integrity (unknown critical chunks rejected as malformed, unknown ancillary chunks ignored, corrupt compressed pixel data categorized as malformed) (matching intrinsic/placement aspects; explicit `none` stretching; meet/slice alignments through the shared viewBox mapping with integral output edges, defaulting to centered meet with presentation attributes winning over inline declarations); clip paths, masks, opacity and the admitted axis-aligned transform subset compose through the same path (rotated image placements stay explicitly unsupported); non-data references, interlaced/color-managed/multi-color images and non-solid palette content stay explicitly unsupported; solid-color 8-bit palette images with PLTE (and optional tRNS) decode through the same uniformity gate; base64, IDAT, palette and transparency copies are budgeted before allocation with transparency lengths validated first, and chunk, decompression and scan work is charged proportionally with bounded cancellation checks. Experimental direct text requires a caller-supplied prepared snapshot; its single-font family, optional weight/style and size may be inherited from `svg`, `g` or `a`, supplied directly/on inline `style`, or resolved through the admitted root stylesheet selectors. Its snapshot validates optional `name` table record/storage bounds and OS/2 version-specific minimum lengths, but keeps the manifest as the source of face family/style selection. It supports scalar `x`/`y`/`dx`/`dy`, scalar `letter-spacing` added only between glyphs and included in anchor width, one validated version-zero horizontal format-zero legacy `kern` subtable or one validated DFLT-script `kern` feature using bounded ordered GPOS PairPos formats 1/2 first-glyph X-advance lookups applied between adjacent glyphs, `text-anchor` (`start`, `middle`, `end`), a bounded diagnostic-only `id`, a bounded `class` token for generic stylesheet selectors, and admitted paint/visibility attributes. GPOS takes precedence over legacy `kern` when both are present. Coverage/class definitions 1/2 and mark-free IgnoreMarks with GDEF are admitted. Distinct lookups accumulate in LookupList order; each uses its first matching subtable, including a zero adjustment. Chained-context format-2 coverage conservatively rejects covered glyphs, including a sole glyph, without executing context rules. Letter spacing inherits through `svg`, `g` and `a` from presentation attributes, inline styles or admitted stylesheet declarations. On direct text, XML `default`/`preserve` whitespace modes retain their existing behavior. CSS `white-space: normal` and `white-space: pre` are admitted on direct text and inherit through `svg`, `g` and `a` from inline or admitted stylesheet declarations; a direct CSS declaration takes precedence over `xml:space`. Nested `tspan` elements concatenate into that run when attribute-free, carrying exactly `xml:space="default"` or `xml:space="preserve"`, carrying scalar `letter-spacing` and/or scalar presentation `dx`/`dy`, carrying a `style` containing only `white-space: normal` or `pre` and/or scalar `letter-spacing`, or carrying an `id`/`class` selected by an admitted generic stylesheet rule containing only those style properties; inline span declarations take precedence over matched rules. Each span inherits its parent mode and letter spacing otherwise. A scalar span `dx`/`dy` shifts the current position before its first retained glyph or that of a descendant; the nearest span that specifies each axis takes precedence, and the shift remains in force for later glyphs. Span spacing applies after its glyphs, including across its following parent boundary, and contributes to the enclosing text chunk's anchor calculation. Scalar span offsets apply after that anchoring adjustment and do not change the anchor width. Other CSS whitespace values, transforms, span paint/font styling, span `x`/`y`, positioning lists, clipping, stroke and other GPOS forms are explicit unsupported features. It is a regression-gated pipeline experiment, not admitted ordinary text or independent-browser-fidelity support |
| PNG encoding | Managed non-interlaced RGBA8 encoder; owned-memory, caller-memory, synchronous-stream and asynchronous-stream output; stream forms aggregate into at-most-64 KiB `IDAT` chunks under a budgeted 64 KiB cap with byte-identical output across all four forms, while memory forms accumulate budgeted segments without geometric copies; stream payloads are written in cancellation-checked chunks of at most 64 KiB |
| Independent RGBA comparison | Exact RGB8/RGBA8 comparison with alpha and mismatch diagnostics |
| Visual comparison CLI | Exact JSON gate for bounded RGB8/RGBA8 PNGs |
| Chrome oracle lifecycle/CDP connection | Hash-pinned Windows CFT archive, transparent capture recipe, bounded graceful CDP shutdown with profile-cleanup retries, three byte-identical fresh-process smoke captures, and retained pinned references for all 601 exact fixture entries; entry count is not a count of fresh capture triples |
| Fixture corpus | 823 original CC0 fixture entries with verified hashes across 457 base documents and 366 variants; all 628 exact entries compare exactly, and 195 other entries retain their observed behavior (168 known-difference renders with pinned-Chrome references and complete comparisons, 27 categorized rejections). The independent Ford matrix is separate: all nine exact against original references/source provenance. These entry counts are not corporate-document coverage. |

### Experimental supplied-font text

Direct text is an experimental, caller-snapshot-only path. Its admitted
single-font family, weight, style and size inherit from `svg`, `g` or `a`
presentation attributes, the admitted root stylesheet selector subset, and inline
declarations, with inline declarations taking precedence. It admits finite
bare-number/`px` scalar position and offset values, bounded text/CDATA extraction,
and `start`/`middle`/`end` anchors. Spans, lists, clipping, strokes and transforms
remain explicitly unsupported. These mechanics do not constitute ordinary text or
browser-fidelity support.

Stylesheet declaration/selector copies retained by rules, definition identifiers
and copied local-reference identifiers reserve their owned string and reservation-
tracker storage before the copy, then release it with parser document state.
The direct-root stylesheet reader incrementally copies text through charged
growing character storage and a charged temporary string, checking work and
cancellation before each read or copy; it releases that temporary string once
stylesheet parsing completes. Reader-provided transient attribute and inline-style
strings remain within the documented conservative reader allowance; this is not
exact BCL-allocation accounting. Font-request discovery reserves ancestor, whitespace, eligibility, group and distinct-request storage from its own preparation budget before allocation and releases it deterministically.
Supported element attributes are validated before stylesheet matching, while the
selector matcher independently enforces the identifier cap; class-selector scans
also observe the shared parser-work limit. Each attribute walked by that policy
also consumes one work unit and checks cancellation.
Every XML-reader event is charged to that same limit, including inert metadata and
the root closing event.
Opaque-background initialization and PNG's per-pixel transparent-RGB normalization
are also charged before those scans run. Opaque-background initialization checks
cancellation every at most 4,096 pixels after allocation.
Every compressed PNG byte produced by the owned/caller-memory buffer and both
streaming IDAT sinks is charged before its copy, checksum, or output write; stream-sink growth copies of already-buffered bytes carry their own work charge.
The isolated Windows probe directly traverses a 64 KiB text node, a full
short-attribute root and 255 × 4 KiB attribute values below that allowance;
those observations are platform/runtime-fingerprinted regression evidence, not a
BCL or process-RSS guarantee. Each retained XML-reader report breaks managed
allocation into setup, traversal and `Name`/`Value` property-access deltas,
and counts text/CDATA nodes without retrieving their values; property access is a subset of
traversal. This makes the probe's attribution inspectable without claiming
cross-runtime exactness. Each child report records its exact input byte count and
SHA-256, which the parent verifies against the launched input. One retained
schema-3 JSON summary then groups every child report under its shared platform/runtime
information, retains the fixed fixture hash and fails mixed runtime/platform data.
Git and CI workflow revisions identify the code; solution sources, scripts and
assemblies are not hashed into reports.
The definition table reserves a two-times requested-capacity allowance before the
BCL selects its prime backing capacity, and verifies that returned capacity is
within the allowance.
Async PNG streaming reserves one small per-conversion scratch buffer before
allocation and reuses it for header and chunk-length framing, including all `IDAT`
chunks.

The target scope and release criteria remain in [PLAN.md](../PLAN.md).
