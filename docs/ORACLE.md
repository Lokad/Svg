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
each candidate hash as well as the pin, executable hash and file version when
exposed by the platform, input/reference hashes, OS/architecture/runtime, locale,
time zone, recipe and headless/GPU/sandbox/viewport policy. It also states the
resource, color-space and font policy: the smoke has no text; Chrome captures its
browser-default PNG output without a DevTools color-space override; and the page
uses data URLs while HTTP(S), file, FTP and WebSocket URL patterns are blocked
through the DevTools Network domain. Each capture CLI has a 120-second deadline by
default (`-CaptureTimeoutMilliseconds` changes it) and is terminated with its
child process tree on timeout.
CI uploads this controlled, fixed-fixture directory on every completed Windows and
Linux job so successful smoke provenance can be reviewed; the broader diagnostic
artifact remains failure-only. No arbitrary user SVG is uploaded.

The setup script refuses an archive or extracted executable whose SHA-256 differs
from the pin; `-Offline` also refuses a cache miss rather than downloading. The
smoke command performs that cache-only verification before capture. This is only browser provisioning; a reviewed canonical reference
still requires the full recipe metadata, repeated-capture stability checks, and
fixture admission.
The current capture recipe is revision 2. It explicitly sets an RGBA-transparent
default background before screenshot capture. Each reference records the recipe
that produced it: retained historical references may therefore record revision 1,
while newly admitted references must record revision 2. A recipe change requires
an explicit review of affected baselines; do not rewrite that provenance field to
make the inventory uniform.
