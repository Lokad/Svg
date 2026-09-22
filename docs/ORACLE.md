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
`capture-chrome.ps1` for an individual exploratory capture.

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
plus a JSON runtime fingerprint. The fingerprint records the capture count and
each candidate hash as well as the pin JSON, archive and executable hashes, the
newline-normalized smoke/setup script source hashes and capture/comparison-tool binary hashes, executable file
version when exposed by the platform, input/reference hashes,
OS/architecture/runtime, locale, time zone, recipe, actual capture deadline and
headless/GPU/sandbox/viewport policy. It also states the resource,
color-space and font policy: the smoke has no text; Chrome captures its
browser-default PNG output without a DevTools color-space override; and the page
uses data URLs while HTTP(S), file, FTP and WebSocket URL patterns are blocked
through the DevTools Network domain. Each capture CLI has a 120-second deadline by
default (`-CaptureTimeoutMilliseconds` changes it) and is terminated with its
child process tree on timeout.
CI uploads this controlled, fixed-fixture directory on every completed Windows and
Linux job so successful smoke provenance can be reviewed; the broader diagnostic
artifact remains failure-only. No arbitrary user SVG is uploaded.

After downloading a successful job's `chrome-smoke-*` and `memory-probe-*`
artifacts into local directories, validate their contents against the same source
revision and Release build with:

```powershell
tools/validate-ci-artifacts.ps1 -Platform Windows -ChromeDirectory path/to/chrome-smoke-windows -MemoryDirectory path/to/memory-probe-windows
tools/validate-ci-artifacts.ps1 -Platform Linux -ChromeDirectory path/to/chrome-smoke-linux -MemoryDirectory path/to/memory-probe-linux
```

The validator checks the current pins, scripts, tools, renderer, fixture and
reference hashes; all three exact PNG captures; and every retained memory-probe
report/input pair. It checks artifact consistency, not GitHub provenance: record
the successful workflow URL, commit and job conclusion separately before calling
the captures remote evidence or admitting a Linux baseline.

Chrome fingerprint and memory-summary schema version 2 define the
newline-normalized script-source hashes. Version 1 artifacts use raw working-tree
script bytes and are not comparable across checkout line-ending policies.

The setup script refuses an archive or extracted executable whose SHA-256 differs
from the pin; `-Offline` also refuses a cache miss rather than downloading. The
smoke command performs that cache-only verification before capture. This is only browser provisioning; a reviewed canonical reference
still requires the full recipe metadata, repeated-capture stability checks, and
fixture admission.
The current capture recipe is revision 3. It first creates a blank target,
configures page/network blocking, emulation and the page background, transparent by
default with explicit white/black inputs (capture-chrome.ps1 -Background white|black, recorded
per reference), then navigates to the data-URL page and waits for its load event
before image decoding and screenshot capture. Each reference records the recipe
that produced it: retained historical references may therefore record revision 1,
2 or 3, while newly admitted references must record revision 3. A recipe change requires
an explicit review of affected baselines; do not rewrite that provenance field to
make the inventory uniform.
