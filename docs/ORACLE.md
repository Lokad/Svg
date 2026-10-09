# Pinned Chrome oracle

The canonical Windows exploratory pin is recorded in
`tools/chrome-for-testing.json`: Chrome for Testing 153.0.8010.52, Chromium
revision 1681091, with archive and extracted-executable SHA-256 values committed
alongside its URL.

`tools/chrome-for-testing.linux64.json` independently pins the matching Linux
x64 archive and executable. It supports a platform-specific smoke check, not a
silent migration of the Windows reference inventory.

Download and verify it into an ignored cache with:

```powershell
tools/setup-chrome.ps1 -CacheDirectory artifacts/chrome
```

Once provisioned, verify that the same archive and executable are usable without
network access:

```powershell
tools/setup-chrome.ps1 -CacheDirectory artifacts/chrome -Offline
```

After a Release build, smoke-test provisioning, capture and exact comparison with:

```powershell
tools/test-chrome.ps1 -CacheDirectory artifacts/chrome -ResultsDirectory artifacts/chrome-smoke
```

The smoke command requires exactly three fresh-process captures; use
`capture-chrome.ps1` for an individual exploratory capture. It also checks that
child failure and a forced parent timeout do not replace an existing output.

On Linux, pass its explicit pin instead:

```powershell
tools/test-chrome.ps1 -CacheDirectory artifacts/chrome -ResultsDirectory artifacts/chrome-smoke -PinPath tools/chrome-for-testing.linux64.json
```

Capture a specific research input without admitting it to the supported corpus:

```powershell
tools/capture-chrome.ps1 -InputPath tests/Research/circle-6x6.svg -OutputPath artifacts/research/circle.png -Width 6 -Height 6 -CacheDirectory artifacts/chrome
```

Repeat this command in separate processes and compare decoded PNGs before adding
or changing research-oracle metadata.

Each smoke output directory receives three independent fresh-process PNG captures,
each exact against the stored smoke reference and byte-identical to the other two,
plus redacted per-launch stderr files and a JSON runtime fingerprint. The fingerprint records the capture count and
each candidate hash as well as the pin JSON, archive and executable hashes,
executable file version when exposed by the platform, input/reference hashes,
OS/architecture/runtime, locale, time zone, recipe, actual capture deadline and
headless/GPU/sandbox/viewport policy. Schema 4 additionally names each retained
stderr file and records parent capture time. Diagnostics are validated directly,
without log hashes. Its child report records Chrome's
lifetime/DevTools-ready time, sandbox-access failures, network-service crashes,
bounded stderr/truncation and profile cleanup. Configured sandbox policy is not
observed startup health: a sandbox-access failure or repeated service crash fails
capture even if Chrome could produce a screenshot. Benign messages do not fail it.
It also states the resource,
color-space and font policy: the smoke has no text; Chrome captures its
browser-default PNG output without a DevTools color-space override; and the page
uses data URLs while HTTP(S), file, FTP and WebSocket URL patterns are blocked
through the DevTools Network domain. The capture CLI has a 30-second cancellation
deadline; the smoke and interactive wrappers impose a 120-second parent deadline
by default (`-CaptureTimeoutMilliseconds` changes it) and terminate their owned
child process tree on timeout. They drain child streams concurrently. Interactive
capture retains a PNG-adjacent .stderr.txt report and publishes through a unique
temporary file, preserving an existing destination on failure.
CI uploads this controlled, fixed-fixture directory on every completed Windows and
Linux job so successful smoke provenance can be reviewed; the broader diagnostic
artifact remains failure-only. No arbitrary user SVG is uploaded.

After downloading a successful job's `chrome-smoke-*` and `memory-probe-*`
artifacts into local directories, validate their contents with the tools from the
same workflow revision:

```powershell
tools/validate-ci-artifacts.ps1 -Platform Windows -ChromeDirectory path/to/chrome-smoke-windows -MemoryDirectory path/to/memory-probe-windows
tools/validate-ci-artifacts.ps1 -Platform Linux -ChromeDirectory path/to/chrome-smoke-linux -MemoryDirectory path/to/memory-probe-linux
```

The validator checks the current browser pin, fixture and reference hashes;
all three exact PNG captures; and every retained memory-probe
report/input pair. It checks artifact consistency, not GitHub provenance: record
the successful workflow URL, commit and job conclusion separately before calling
the captures remote evidence or admitting a Linux baseline.

Chrome fingerprint schema 4 requires the startup-health diagnostics; the memory
summary remains schema 3. Reports identify inputs, outputs, browser and runtime;
Git and CI workflow revisions identify the code; upstream source uses pinned
revisions and URLs. Hashes of source files, extracted snippets, scripts, solution
assemblies, diagnostic reports or logs are not recorded or required.
Old Chrome schema-3 fingerprints do not establish observed
sandbox health and are rejected by the current validator.

The setup script refuses an archive or extracted executable whose SHA-256 differs
from the pin; `-Offline` also refuses a cache miss rather than downloading. The
smoke command performs that cache-only verification before capture. This is only browser provisioning; a reviewed canonical reference
still requires the full recipe metadata, repeated-capture stability checks, and
fixture admission.

## Windows sandbox permissions and launch health

Setup uses the pinned Chrome installer's
[`--configure-browser-in-directory` operation](https://chromium.googlesource.com/chromium/src/+/d1e2de40efe0ac0ca9b5c05b76ea1288fd4fc0f6),
also used by [Puppeteer's Windows provisioning](https://pptr.dev/troubleshooting#chrome-reports-sandbox-errors-on-windows).
It configures the supplied browser directory without a normal browser installation.
Chromium's CONFIGURE_APP_CONTAINER_SANDBOX_SUCCESS status is 78 (not just zero).

The script verifies the archive and chrome.exe pins, authenticates cached setup.exe
against that archive, rejects path escapes and junction/symlink traversal, and
uses a bounded hidden process to configure only the resolved browser directory.
It checks the Chrome-specific chromeInstallFiles and lpacChromeInstallFiles
read/execute capability rules on the directory and all resources after setup.
The same checks run for fresh, cached and offline use. Existing ACLs and parent
directories are preserved; denied access/protected inheritance is reported rather
than reset or worked around with broad grants. No Everyone/ALL APPLICATION
PACKAGES grant, sandbox-disable flag, network-service bypass or pin change is used.

Native Chrome stdout is drained separately from the PNG transport. At most
32 KiB of child stderr is retained; error recognition continues after truncation
and across read boundaries. Data/remote URLs and local user/temp prefixes are
redacted. After shutdown, the capture must have healthy startup diagnostics and
successful profile cleanup before screenshot bytes are returned. DevTools port
file creation, sharing locks and partial writes are retried inside the deadline.
Only owned processes/profiles are cleaned; unrelated browsers are untouched.

After building, run the setup boundary/permission scenarios and the real smoke:

```powershell
tools/test-chrome-setup.ps1 -ResultsDirectory artifacts/chrome-setup-tests
tools/test-chrome.ps1 -CacheDirectory artifacts/chrome -ResultsDirectory artifacts/chrome-smoke
tools/test-validate-ci-artifacts.ps1 -Platform Windows -ResultsDirectory artifacts/validator-selftest
```

Repair validation on 2026-09-26, Windows x64:

- Fresh extraction from the verified cached archive, offline reuse and repeated
  setup passed; browser ACLs were idempotent and the cache-parent ACL was unchanged.
- Three cached-install smoke captures and three fresh-install captures were exact
  against the existing red reference, with zero sandbox failures/network crashes
  and successful profile cleanup. Cached Chrome lifetimes were 1,243/1,342/1,390 ms
  (ready in 311/318/349 ms); fresh lifetimes 1,925/1,458/1,546 ms
  (ready in 929/336/487 ms). These are local observations, not matched before/after
  benchmarks or Linux/remote-CI evidence.
- Setup tests cover escapes/junctions, missing/denied RX and installer failure codes.
  C# tests cover bounded/chunk-split/post-truncation diagnostics, benign output,
  repeated crashes, redaction, cancellation and partial port files. Validator
  negatives reject missing/forged health, crashes, leaked profiles and bad timings.
- No existing SVG/reference pixels or capture recipe were changed.
- The circle and symbol-pattern logo references were independently recaptured
  three times each and remained exact, with no new profile directories. This
  checks oracle stability, not the candidate renderer's known circle mismatch.

The current capture recipe is revision 3. It first creates a blank target,
configures page/network blocking, emulation and the page background, transparent by
default with explicit white/black inputs (capture-chrome.ps1 -Background white|black, recorded
per reference), then navigates to the data-URL page and waits for its load event
before image decoding and screenshot capture. Each reference records the recipe
that produced it: retained historical references may therefore record revision 1,
2 or 3, while newly admitted references must record revision 3. A recipe change requires
an explicit review of affected baselines; do not rewrite that provenance field to
make the inventory uniform.

## Capture sizing behavior

The harness renders the SVG in an unstyled block img, see tools/Lokad.Svg.Chrome/ChromeSvgCapture.cs,
and screenshots the page top-left at the requested dimensions with captureBeyondViewport=false.
Size every capture from the SVG intrinsic dimensions:

- Roots with explicit width and height render the img at that intrinsic size. A smaller capture crops
the top-left; a larger one adds page-background margin. Compare candidates only at intrinsic size.
The batch-101 inv-080 proof shows a 540x360 capture of the intrinsic-600x400 file is a top-left crop,
with vertical-bar center x=111 and stripe scale 22.222, so its 86,055-pixel divergence from the
meet-fitted candidate is the viewport contract, not renderer fidelity.

- ViewBox-only roots without dimensions size the auto img by ratio negotiation. The batch-83 inv-011
proof shows Chrome negotiates height from the intrinsic ratio and screenshots the top while the renderer
meets the viewBox into its viewport. Compare at explicit integer-scale viewports with matching aspect
ratio instead, as in batches 84-89 and 101-103.

- Fractional viewBox ratios can negotiate fractional CSS image dimensions even
  when the requested screenshot dimensions are rounded to a nearby aspect ratio.
  The retained tiger's auto image occupies 361.984375×295.796875 CSS pixels inside
  its 362×296 capture. That historical PNG stays diagnostic. For an explicitly
  declared 362×296 conversion viewport, a separately recorded root-only width/
  height materialization gives the browser the same frame; convert the unchanged
  original with explicit viewport options. Keep the old source/PNG/provenance,
  distinguish the new acceptance reference, and qualify it before admission.
  Do not change product fitting to reproduce page layout negotiation or cropping.

- Hard capture limits: SVG input at most 8 MiB, each side at most 16,384 pixels,
  and capture area at most 8,388,608 pixels. Screenshot replies alone admit
  64 MiB of assembled UTF-8 JSON, including PNG base64; commands, other replies
  and retained events remain bounded at 16 MiB. Fragmentation and cancellation
  are checked while receiving. The old 3000x2000 request failed at the historical
  two-million-pixel cap (batch-103); its dimensions are now admitted.

## Frozen corporate captures

After a Release build, capture every frozen case, or a named subset:

```powershell
tools/capture-corporate-cohort.ps1 -OutputDirectory artifacts/corporate-captures -CacheDirectory artifacts/chrome
tools/capture-corporate-cohort.ps1 -OutputDirectory artifacts/corporate-large -CacheDirectory artifacts/chrome -CaseId inv-020-large-transparent,inv-026-large-transparent
tools/capture-corporate-cohort.ps1 -OutputDirectory artifacts/corporate-linux -CacheDirectory artifacts/chrome-linux -PinPath tools/chrome-for-testing.linux64.json
```

The opt-in script verifies the frozen version/revision/recipe, the chosen platform
pin and input hashes. It authenticates the browser installation once per batch and launches three
sequential fresh processes per configuration, and retains PNGs, launch stderr and
`captures.json` under the supplied output directory. Failures remain recorded;
only stable healthy triples qualify as references. Each completed case publishes
the updated manifest atomically so readers see a complete JSON record.
It preserves the declared
geometry, sizing and backgrounds. Capturing an image does not establish renderer
fidelity. Ordinary `CorporateCohortReferenceTests` compare the admitted reference
matrix offline, including independently captured renderer rejections.
Run `test-chrome.ps1` without concurrent capture batches on the same host:
its cleanup check compares the shared temporary-profile namespace before and after.

Measure the renderer against those captures with the existing VisualDiff tool:

```powershell
dotnet tools/Lokad.Svg.VisualDiff/bin/Release/net10.0/Lokad.Svg.VisualDiff.dll --cohort . artifacts/corporate-captures artifacts/corporate-report
```

The command verifies the immutable variant options, pin, input identities and all
three actual capture PNGs. For cases absent from a fresh batch it can use a
verified banked reference from the same platform. A missing or failed Linux
capture cannot borrow a Windows reference. The summary records the actual
capture pin. It renders every declared variant with default SDK
limits and writes candidate PNGs plus `summary.json` under the report directory.
The report separates capture failures, missing references, renderer rejections,
exact pixels and known differences; its document numerator requires every
declared variant to be exact. Comparisons include directional alpha errors,
opaque/hole changes and pixel witnesses. Exit zero means measurement completed,
including categorized differences and rejections; the two-PNG mode remains an
exactness gate. Neither command rewrites the selection or admits references
automatically. Reports contain input/output hashes, with Git identifying code.

The completed 2026-10-06 matrix has all 258 references: 60 exact rendered variants,
180 known differences and eighteen renderer rejections. The 225 remaining cases
have 675 stable healthy fresh captures; all 33 earlier references and all 240
candidate PNGs are unchanged. Only Ford and YouTube are exact in every variant.
Detailed local captures and comparisons are under
`artifacts/cohort-complete-20261006/`.

The 2026-10-06 transport checkpoint supplies all fifteen formerly blocked large
references. Forty-five captures are stable, healthy and cleaned; eighteen
Ford/YouTube overlap captures preserve all six original reference identities.
A separate 4096x2048 noise capture yields a 32,270,774-byte PNG, requiring more
than 41 MiB of base64 in the reply, with healthy diagnostics and cleanup.
The browser pin and recipe 3 remain unchanged.
