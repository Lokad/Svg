# Developer tools and research

Run commands from the repository root after a Release build. Keep caches, captures
and reports under ignored `artifacts/`. Browser tooling is development-only;
conversion has no browser dependency. [Tests and provenance](../tests/README.md)
defines collection membership and fixture admission.

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
- [ChromeSvgCapture](Lokad.Svg.Chrome/ChromeSvgCapture.cs) renders an unstyled block
  `img` and screenshots the page's top-left with `captureBeyondViewport=false`.
  Explicit root dimensions determine intrinsic size: smaller screenshots crop,
  larger ones add background. ViewBox-only inputs negotiate image size by ratio;
  use matching-aspect integer-scale viewports. Fractional ratios can still negotiate
  fractional CSS sizes. If explicit conversion sizing needs a root-only capture
  materialization, declare it separately, retain the original and old reference,
  and qualify the matching viewport contract before admission.
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
dotnet run --project tools/Lokad.Svg.VisualDiff -- reference.png candidate.png
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

## Smoke checks and CI evidence

The memory smoke cancels synchronously when background initialization starts
(`--cancel-after-background-start-ms 0`), so its phase assertion is independent
of timer scheduling. Positive delays remain available for manual diagnostics;
they may cancel in a later phase.

The Chrome smoke requires exactly three fresh-process captures, each exact
against its stored reference and byte-identical to the others. It also checks
child failure/parent timeout, destination preservation and profile/root cleanup.

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
