# Developer tools and research

Run commands from the repository root after a Release build with the pinned
.NET 11 RC SDK. Test-referenced tools build both targets; direct `dotnet run`
commands select `--framework net11.0`. Smoke/capture/validation scripts default to
`net11.0` and accept
`-TargetFramework net10.0` after an explicit .NET 10 build. Install both runtimes.
`test-package.ps1` checks and executes both targets against one isolated package
restore; no runtime roll-forward is required. Keep caches, captures
and reports under ignored `artifacts/`. Browser tooling is development-only;
conversion has no browser dependency. [Tests and provenance](../tests/README.md)
defines collection membership and fixture admission.

Package ZIP timestamps are fixed at `1980-01-01T00:00:00Z` in the library project.
To check reproducibility, pack unchanged Release outputs into two directories at
different times and compare both `.nupkg` and `.snupkg` bytes. NuGet's
`DeterministicTimestamp=true` uses the current time; a fixed timestamp is required.

## Development and qualification

Use the [three test responsibilities](../tests/README.md) to choose a short edit
loop: direct mechanism checks first, then representative API/ownership contracts
and affected native rendering. Most images use one canonical conversion call.
Add another API path only for a specific interaction or allocation contract.
Running thousands of fixtures through all twelve API forms is optional
exploratory coverage; agreement between API forms cannot establish native
correctness. Retain the exploratory results separately from regression evidence.

The extended color/filter context matrices are selected by `Category=Research`;
`Category!=Research` omits them for development. Both preserve individual case
discovery. Full unfiltered Windows/Linux tests, resource/font-host probes and
installed-package checks remain milestone/CI gates. Keep browser captures and
references unchanged when reorganizing tests.

`test-memory-probe.ps1` exercises the shared XML-reader policy in conversion and
allocation modes. Reader workspace reports include peak admitted prolog storage;
managed allocation totals are separate observations. Memory artifact schema 5
requires inert-DOCTYPE, nested internal-entity and external-entity rejection
parity, checked by
`test-validate-ci-artifacts.ps1`; use the validator from the producing commit.

For the [bilinear numeric probe](../tests/Research/svgweb/highp-bilinear.js),
evaluate the script with DevTools `Runtime.evaluate` in the pinned browser and
retain the returned rows. Color-dodge over transparent black selects the floating
sampling pipeline without changing the observed alpha; ordinary source-over uses
a different bitmap path. The [capture record](../tests/Lokad.Svg.Tests/TestData/highp-bilinear.json)
keeps the source/data hashes and both hosts' fresh-process evidence.

On Linux, compile the [native source-stage control](../tests/Research/svgweb/native-source-stages.cpp)
with `g++ -std=c++20 -O2 -ffp-contract=off tests/Research/svgweb/native-source-stages.cpp -o artifacts/native-source-stages`.
Run `artifacts/native-source-stages` and compare its JSON with the
[committed vectors](../tests/Lokad.Svg.Tests/TestData/native-source-stages.json).
The [canvas-recording control](../tests/Research/svgweb/native-canvas-recording.cpp)
uses the same compiler flags and emits the separate
[canvas vectors](../tests/Lokad.Svg.Tests/TestData/native-canvas-recording.json).
Both controls preserve native float operations; keep contraction disabled.

Keep intermediate floating-point rounding enabled; this checks arithmetic stages
separately from browser reference admission.

The [bitmap-restore control](../tests/Research/svgweb/native-device-restore.cpp)
uses the same compiler flags. Run it and compare its JSON values with the
[device-axis vectors](../tests/Lokad.Svg.Tests/TestData/native-device-restore.json);
forward/inverse float rounding determines whether restoration retains affine skew.

The [picture-repeat control](../tests/Research/svgweb/pattern-repeat-native.cpp)
uses the same compiler flags. Compare its CSV with the
[committed tap vectors](../tests/Research/svgweb/pattern-repeat-native.csv).
These pin floating repeat and gather addresses directly; the
[browser control](../tests/Research/pattern-repeat/README.md) qualifies their rendering interaction.

## Generated output and retention

Organize generated data by purpose: pinned tool caches, current qualification,
release evidence and benchmark results. Keep source inputs, independent platform
references, licenses and reproducible recipes committed. Ordinary builds and tests
must work without historical run directories or external checkouts.

Retain published packages and symbols with their exact source identity and hashes.
Before retiring a run, promote unresolved original inputs, options and provenance;
equal resulting images do not establish duplicate evidence. Compact completed
qualification into its committed responsibility record after matching CI succeeds.

Remove copied assets only after comparing every file with retained committed
sources. An old tool installation can be removed after checking its files against
a retained archive and confirming no process or configuration uses that path.
Keep the archive, hash, version and notices when historical reproduction needs it.
Age and size alone do not establish that output is disposable.

Check resolved removal targets and reparse points before cleanup. Use
`git worktree remove` for a registered checkout after verifying it is clean, unused
and its commit remains reachable. Keep temporary qualification exports until their
checks finish and their evidence is retained.

## Export the visual suite

Ordinary builds share the checkout's committed research corpus. To produce a
standalone offline suite after a Release build:

```powershell
tools/export-visual-tests.ps1 -DestinationDirectory artifacts/visual-suite-net11
```

The destination must be new. Use `-TargetFramework net10.0` after building that
framework. The export contains the visual-test binaries, independent corpus,
font licenses and FontProbe host. `test-bundle.json` records the source/tree IDs,
modified-source flag and actual assembly hashes. Move the complete directory to
the qualification host, install its matching runtime and SDK, then run
`dotnet vstest Lokad.Svg.VisualTests.dll` from there. No checkout, browser,
network endpoint or external repository is needed. Inside a checkout,
`LOKAD_SVG_RESEARCH_ROOT` can explicitly select the exported `Research` path;
otherwise tests prefer current committed sources to stale output copies.

## Chrome setup

The [Windows pin](chrome-for-testing.json) and [Linux pin](chrome-for-testing.linux64.json)
own the browser version, revision, download URL, archive/executable SHA-256 and
capture recipe. Setup rejects hash mismatches; `-Offline` also rejects cache misses.

```powershell
tools/setup-chrome.ps1 -CacheDirectory artifacts/chrome
tools/setup-chrome.ps1 -CacheDirectory artifacts/chrome -Offline
tools/test-chrome.ps1 -CacheDirectory artifacts/chrome -ResultsDirectory artifacts/chrome-smoke
```

On Linux, pass `-PinPath tools/chrome-for-testing.linux64.json` to setup, smoke
and capture commands. Each platform needs its own references; a missing Linux
capture cannot borrow Windows evidence. Run the smoke without concurrent capture
batches on the same host because it audits the shared temporary-profile namespace.

Setup preserves sandboxing. On Windows it authenticates the bundled installer,
uses its bounded `--configure-browser-in-directory` operation (success status 78)
and verifies Chrome-specific read/execute capability rules on the browser tree.
DevTools connects directly over loopback, independently of host HTTP/WebSocket
proxy settings. The three-process smoke supplies an unusable proxy to protect that
contract while comparing the same native pixels.
Fresh, cached and offline paths reject escapes/junctions and preserve parent ACLs;
denied permissions are reported rather than broadly reset. Ubuntu CI grants
`userns` through an AppArmor policy attached to the exact pinned executable; see
the [workflow](../.github/workflows/ci.yml). Sandbox health is checked after launch,
not inferred from configuration.

## Capture and reference rules

Capture an individual input without admitting it to supported coverage:

```powershell
tools/capture-chrome.ps1 -InputPath tests/Research/circle-6x6.svg -OutputPath artifacts/research/circle.png -Width 6 -Height 6 -CacheDirectory artifacts/chrome
```

- Capture each proposed reference in **three fresh processes**, require
  byte-identical triples and exact decoded pixels, and retain the first capture
  with its provenance. Record background (`transparent`, `white` or `black`),
  device scale, dimensions, platform pin, recipe and input/resource hashes.
- Recipe 3 configures blocking, emulation and page background on a blank target
  before navigation, then waits for load and image decoding. Historical references
  keep their actual recipe 1/2/3; new admission uses the pinned current recipe.
  A recipe change requires review of affected baselines, not metadata relabeling.
- Canvas `drawImage`/`toDataURL` probes use a different raster path. Their dither
  phase can differ from page screenshots across compositor seams. Keep them as
  exploratory evidence until each proposed reference is independently qualified
  through `ChromeSvgCapture`; preserve naturally unequal profiles separately.
- [ChromeSvgCapture](Lokad.Svg.Chrome/ChromeSvgCapture.cs) ordinarily renders an
  unstyled block `img` and screenshots the page's top-left with
  `captureBeyondViewport=false`.
  Explicit root dimensions determine intrinsic size: smaller screenshots crop,
  larger ones add background. ViewBox-only inputs negotiate image size by ratio;
  use matching-aspect integer-scale viewports. Fractional ratios can still negotiate
  fractional CSS sizes. If explicit conversion sizing needs a root-only capture
  materialization, declare it separately, retain the original and old reference,
  and qualify the matching viewport contract before admission.
- For an explicit image viewport, pass both `-ImageViewportWidth` and
  `-ImageViewportHeight` to `capture-chrome.ps1`; the stdin CLI accepts
  `--image-viewport <CSS-width> <CSS-height>` after its optional background.
  This sets the image element's CSS width/height while preserving SVG bytes.
  Screenshot width/height remain independent crop/padding dimensions, not an
  output scale. Record both viewports and the named mode in provenance; do not
  relabel intrinsic-image references. Use the renderer's actual rounded raster
  size rather than a ceiling. Fractional image sizes can add native resampling;
  qualify them separately before treating a difference as a renderer bug.
- To vary output density while preserving that CSS image viewport, also pass
  `-OutputScale <density>`; the stdin CLI accepts `--output-scale <density>` after
  `--image-viewport`. Width/height are physical PNG pixels. Device emulation uses
  that density, with a CSS screenshot clip of `width/density` by `height/density`
  and an integer page viewport enclosing the clip. Record all three separately.
  [Image boundary controls](../tests/Research/root-image-density/capture.json)
  qualify magnification, minification and fractional coverage on both platforms.
  Resizing the CSS image changes layout and cannot stand in for this mode.
  Pinned Chrome truncates the CSS screenshot clip before rounding to device pixels;
  unrepresentable physical crops fail before launch. For example, at density 2 use
  an explicit 36×24 capture to inspect a 35×23 output region. Record the comparison
  window separately; preserve the enclosing native PNG without resizing it.
- Capture limits are 8 MiB SVG input, 16,384 pixels per side and 8,388,608 pixels
  total. Screenshot JSON transport allows 64 MiB including PNG base64; other
  replies/commands/events remain bounded at 16 MiB. These limits belong to the
  harness, not the [production renderer](../docs/SUPPORT_MATRIX.md#resource-limits-and-memory-accounting).
- HTTP(S), file, FTP and WebSocket URL patterns are blocked through DevTools.
  Captures use embedded resources and browser-default PNG color output, without
  a DevTools color-space override. Text references need the declared supplied
  programs, not accidental machine-font substitution.

The capture CLI has a 30-second cancellation deadline; PowerShell wrappers impose
a 120-second parent deadline by default (`-CaptureTimeoutMilliseconds` overrides
it). They drain child streams concurrently, terminate their owned process tree on
timeout and reclaim owned profiles/scratch directories. Interactive capture keeps
a PNG-adjacent `.stderr.txt` report and publishes through a unique temporary file,
preserving an existing destination on failure.

Launch diagnostics retain at most 32 KiB stderr with URL/local-path redaction;
failure recognition continues across chunks and after truncation. Sandbox-access
failure, repeated network-service crashes or failed profile cleanup reject capture
even if a screenshot exists. Benign messages do not reject it.

## Pixel comparison

```powershell
dotnet run --framework net11.0 --project tools/Lokad.Svg.VisualDiff -- reference.png candidate.png
```

Exit codes are `0` for equal dimensions and exact canonical RGBA8 pixels, `1`
for mismatch and `2` for usage/decoding errors. RGB is ignored only where both
pixels have zero alpha; all partial-alpha differences fail. PNG compression need
not match. JSON `PixelComparison` output includes changed pixels, maximum/mean/RMS
error, separate alpha error and mismatch bounds.

The independent decoder accepts bounded non-interlaced RGB8/RGBA8 PNGs without
`tRNS` transparency; convert these images to RGBA8 first. It rejects malformed
headers/chunk ordering and unknown critical chunks, and checks CRCs and decoded
storage sizes. Format expansion must remain independent of the
production image decoder. Tolerance or candidate-driven reference replacement
does not close an admitted failure.

## Research probes

1. Isolate one variable in a minimal CC0-original SVG, preferably a 16×16 canvas.
   Keep other inputs admitted and ordinary; rejection by an unrelated feature
   gate cannot calibrate the behavior being studied.
2. Follow the [capture rules](#capture-and-reference-rules), group output names by
   probe and verify the triple's hashes before retaining the first reference.
3. Add the SVG, reference and complete oracle block to the relevant manifest.
   Compute lowercase hashes from actual staged files, validate JSON and audit
   hashes before testing. Record `candidateError` for rejections or
   `admittedRendering` measurements for renders, including residual differences.
4. Run the relevant bank tests. [ResearchProbeTests](../tests/Lokad.Svg.VisualTests/ResearchProbeTests.cs)
   checks SPDX/license, source/reference hashes, capture metadata, dimensions and
   exact recorded outcomes. Its rendering measurements fail on drift in either
   direction; reference capture alone is not a renderer pass.
5. Graduate useful exact probes into the fixture corpus with source, reference
   and provenance retained. Remove the research entry only after the fixture
   gate covers it; preserve the positive control.

Correct shared mechanisms and preserve established exact results. Run targeted
checks during correction and full qualification at meaningful checkpoints. Keep
questions, measurements, negative results and rejected hypotheses in local study
artifacts; summarize conclusions in the change description. Update
[the design](../docs/RASTERIZATION_DESIGN.md) only for lasting architectural changes
and [the test guide](../tests/README.md) only for collection/admission changes.

## Hosted SVGWeb inventory

The optional standard-library Python procedure inventories every direct `.svg`
entry in the W3C-hosted SVGWeb directory. It fetches at most four documents at a
time, caps each response at 8 MiB and writes JSON metadata only. It never writes
SVG bodies or fetches their external dependencies. Select a new output path:

```powershell
python -B tools/inventory-svgweb.py artifacts/research/svgweb-inventory.json
```

Compare names and original byte hashes with the
[committed ledger](../tests/Research/svgweb/originals.json); additions, removals and
changed inputs need explicit accounting. HTTP/XML failures stay in the output.
Tag/attribute counts, lexical `font-family` declarations and external-reference
uses support triage; they do not resolve CSS, prove dependencies available or
establish rendering correctness. This live-endpoint procedure is outside normal
builds/tests. The [review record](../tests/Research/svgweb/README.md) defines the
remaining original-rendering and orthogonal-fixture work.

For the seven legacy Droid SVG fonts, an optional research transport derives
unhinted TrueType programs from the hashed source outlines and advances. It
requires `fontTools==4.66.1` in a development Python environment. Choose a new
ignored destination; no hosted XML is retained or installed:

```powershell
python -B tools/derive-svgweb-fonts.py --output-directory artifacts/research/svgweb-fonts
```

All 96 glyphs per font, including the missing glyph, must round-trip exactly.
Keep the source's implicit normal/400 descriptors; filenames do not establish
weight or style. Use the generated catalog with FontProbe and embed the identical
program into native capture with `@font-face`. Preserve native/library input hashes
and logical viewport. This transport qualifies an explicit program context; it
does not establish native SVG-font support or whole-original acceptance. The
[font record](../tests/Research/svgweb/font-contexts.json) separates these results
from configured generic aliases and private, unredistributed Arial programs.

The [supplied-original record](../tests/Research/svgweb/supplied-original-contexts.json)
pins all nineteen request sets, program identities, native aliases, viewports and
input/output hashes. Use explicit FontProbe catalogs/providers; bind the same bytes
and descriptors with native `@font-face`. Match initial generic families as well
as named requests. Preserve comments, processing instructions and nonfont content
when materializing the native input in RAM; verify the inverse font changes restore
the parsed original. Each host/context needs three healthy, profile-cleaned,
terminal captures with identical PNG hashes. These font bindings qualify only the
declared contexts; keep legacy SVG-font semantics separate. Private licensed font
bytes stay outside the repository and packages.

Keep CSS image resizing separate from canvas drawing destinations. The committed
[root-boundary recipe](../tests/Research/svgweb/root-image-boundary.json) specifies
its fractional canvas extent and independent native triples; substituting the same
numbers as CSS image dimensions changes the capture context.

For numerical stage inspection, enable DevTools `LayerTree` before navigation,
snapshot painted layers with `makeSnapshot`, and read `snapshotCommandLog` and
`replaySnapshot`. Release every snapshot. Retain matrices, saveLayer bounds and
command hashes; discard hosted path operands. Record unavailable layer logs
explicitly. The [projection controls](../tests/Research/svgweb/root-image-projection.json)
include owned inputs and exact observed coefficients; their larger white page is
a numerical diagnostic, separate from canonical rendering-reference admission.

## Font program controls

The CC0 [extension fixture](../tests/Research/kerning-flags/README.md) derives from
the committed GposMarks program. With development-only `fontTools==4.66.1`, rebuild
it into a new output file:

```powershell
python -B tests/Research/kerning-flags/derive-extension.py --output artifacts/research/GposExtension.ttf
```

The producer verifies the source hash and unchanged glyph geometry. Compare its
reported program hash with [the capture record](../tests/Research/kerning-flags/extension.json).
Reference capture remains independent and requires fresh healthy platform triples.
The [conditional-target programs](../tests/Research/windows-conditional-targets/README.md)
have an independent CC0 recipe using the same development dependency. Rebuild into
a new destination and require both recorded hashes:

```powershell
python -B tests/Research/windows-conditional-targets/derive.py --output-directory artifacts/research/conditional-targets
```

The [small-strike controls](../tests/Research/windows-small-strikes/README.md)
separate program admission, scaled children and scanner thresholds. The same
dependency reproduces all five original CC0 font hashes:

```powershell
python -B tests/Research/windows-small-strikes/derive.py --output-directory artifacts/research/small-strikes
```

The [legacy-declaration control](../tests/Research/legacy-font-declarations/README.md)
borrows that program. With FontProbe, use its `font-pack.json` and
`--font-root tests/Research/windows-small-strikes`; no additional font is generated.

For original font comparisons, match initial generic-family selections as well as
authored named families; the library's legacy last resort and browser ambient serif
can select different programs. Preserve both input hashes and catalog bindings.

## Smoke checks and CI evidence

The memory probe supplies a host budget with `--memory-budget-bytes`; it does not
configure a renderer-private cap. Owned output keeps its charge through the write.
The memory smoke cancels synchronously when background initialization starts
(`--cancel-after-background-start-ms 0`), so its phase assertion is independent
of timer scheduling. Positive delays remain available for manual diagnostics;
they may cancel in a later phase.

The Chrome smoke requires exactly three fresh-process captures, each exact
against its stored reference and byte-identical to the others. It also checks
child failure/parent timeout, destination preservation and profile/root cleanup.
One additional viewport/crop control reuses an independent transparent reference
and fails if the explicit image viewport is ignored. Process shutdown observes
native exit separately from redirected-pipe completion, with bounded termination,
drain and profile cleanup before accepting any PNG.
On a temporary-root mismatch, `wrapper-cleanup-failure.json` records added and
removed roots and bounded immediate entries. Preserve that report before retrying.

```powershell
tools/test-chrome-setup.ps1 -ResultsDirectory artifacts/chrome-setup-tests
tools/test-validate-ci-artifacts.ps1 -Platform Windows -ResultsDirectory artifacts/validator-selftest
```

Successful smoke output includes PNGs, redacted per-launch stderr and a schema-4
runtime fingerprint: pin/archive/executable and input/reference/candidate hashes,
version/runtime/platform, locale/time zone, recipe, deadlines, capture timings and
viewport/GPU/sandbox/resource/color/font policies. Launch reports record observed
startup health, DevTools-ready/lifetime timings, truncation and cleanup. Schema-3
Chrome fingerprints lack observed health and are rejected; the memory summary
still uses schema 3.

CI retains fixed-fixture smoke artifacts on completed Windows/Linux jobs; it does
not upload arbitrary user SVGs. Download `chrome-smoke-*` and `memory-probe-*`,
then validate with tools from the same workflow revision:

```powershell
tools/validate-ci-artifacts.ps1 -Platform Windows -ChromeDirectory path/to/chrome-smoke-Windows -MemoryDirectory path/to/memory-probe-Windows
tools/validate-ci-artifacts.ps1 -Platform Linux -ChromeDirectory path/to/chrome-smoke-Linux -MemoryDirectory path/to/memory-probe-Linux
```

Validation checks pins, hashes, exact captures and retained memory reports/inputs.
Recorded OS descriptions and host runtimes must agree between the Chrome and
memory artifacts; the reviewer's OS and runtime may differ. CI adds
`-RequireCurrentHost` to also match those records against the producing worker.
It establishes artifact consistency, not GitHub provenance: record the successful
workflow URL, commit and job conclusion separately. Git/CI identify code; hashes
identify assets/browser/output. Source snippets, assemblies and logs need no
additional identity hashes.

For resource, package and isolated-conversion commands, see the repository
[diagnostics](../README.md#diagnostic-tools). [FontProbe](Lokad.Svg.FontProbe/README.md)
documents explicit local/HTTP font acquisition and host responsibilities.
The package smoke script checks inline README file links before building
and running its clean installed-package consumer.

## Pinned Unicode data

Run `python -X utf8 tools/generate-unicode-case-data.py` to regenerate the
[Unicode table](../src/Lokad.Svg/Parsing/SvgTextCasing.Data.cs) and its
[direct mapping corpus](../tests/Research/text-case-mapping/unicode-data.json).
The script verifies pinned Unicode 17 source hashes and keeps downloads in memory.
It includes upper/lower mappings, lowercase-initial titlecase mappings and pinned
casing/word-context properties, soft-dotted characters and canonical combining
classes needed by conditional language rules.
Run `python -X utf8 tools/generate-unicode-canonical-data.py` for the separate
[canonical shaping table](../src/Lokad.Svg/Fonts/SvgCanonicalText.Data.cs) and its
[source hashes](../tests/Research/text-case-mapping/unicode-canonical-data.json).
It pins combining classes, recursive decompositions, composition exclusions and
default-ignorable properties, using the same hash-checked, memory-only downloads.
It does not capture rendering references. Data licensing is included in
[third-party notices](../THIRD_PARTY_NOTICES.txt); the [case bank](../tests/Research/text-case-mapping/README.md)
describes native admission and the independent literal controls.

## Native math reference profiles

The [reference probes](../tests/Research/native-math-profiles/README.md) are optional
Linux research tools. From the repository root:

```bash
gcc -O2 -ffp-contract=off -fno-builtin -Wall -Wextra -Werror tests/Research/native-math-profiles/atan2-profile.c -ldl -o artifacts/atan2-profile
g++ -std=c++20 -O2 -ffp-contract=off -Wall -Wextra -Werror tests/Research/native-math-profiles/endpoint-probe.cpp -o artifacts/endpoint-probe
g++ -std=c++20 -O2 -ffp-contract=off -Wall -Wextra -Werror tests/Research/image-fit-sampling/native-spans.cpp -o artifacts/image-spans
artifacts/image-spans > artifacts/image-spans.json
artifacts/atan2-profile
python3 -c 'import json; rows=json.load(open("tests/Lokad.Svg.Tests/TestData/endpoint-arc-native.json"))["cases"]; [print(r["id"], *r["p"]) for r in rows]' | artifacts/endpoint-probe
```

Keep outputs separate from frozen data. To compare libm versions, use verified
distribution libraries in an isolated mount namespace, without installing them.
Preserve normal `/proc` and `/dev` mounts for Chrome's existing sandbox. Keep the
pinned executable, launch flags, source/font hashes and capture recipe unchanged;
require three healthy, profile-cleaned native captures for each new companion.
Record package checksums, compiler flags and native results. Ordinary tests need
neither a native compiler nor another installed libc.
