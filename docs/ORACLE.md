# Pinned Chrome oracle

The canonical Windows exploratory pin is recorded in
`tools/chrome-for-testing.json`: Chrome for Testing 153.0.8010.52, Chromium
revision 1681091, with the archive SHA-256 committed alongside its URL.

Download and verify it into an ignored cache with:

```powershell
tools/setup-chrome.ps1 -CacheDirectory artifacts/chrome
```

The setup script refuses an archive whose SHA-256 differs from the pin. This is
only browser provisioning; a reviewed canonical reference still requires the
full recipe metadata, repeated-capture stability checks, and fixture admission.
