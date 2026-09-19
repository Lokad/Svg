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

After a Release build, smoke-test provisioning, capture and exact comparison with:

```powershell
tools/test-chrome.ps1 -CacheDirectory artifacts/chrome -ResultsDirectory artifacts/chrome-smoke
```

On Linux, pass its explicit pin instead:

```powershell
tools/test-chrome.ps1 -CacheDirectory artifacts/chrome -ResultsDirectory artifacts/chrome-smoke -PinPath tools/chrome-for-testing.linux64.json
```

The setup script refuses an archive or extracted executable whose SHA-256 differs
from the pin. This is only browser provisioning; a reviewed canonical reference
still requires the full recipe metadata, repeated-capture stability checks, and
fixture admission.
The current capture recipe is revision 2. It explicitly sets an RGBA-transparent
default background before screenshot capture. Each reference records the recipe
that produced it: retained historical references may therefore record revision 1,
while newly admitted references must record revision 2. A recipe change requires
an explicit review of affected baselines; do not rewrite that provenance field to
make the inventory uniform.
