# Rasterization research probes

These are original CC0 calibration inputs, not supported-product fixtures. Each
probe has a pinned-Chrome reference in `references/` and is deliberately asserted
to fail public conversion with `UnsupportedFeature`. They preserve reproducible
evidence for P1.3 geometry and paint-server work without turning an observed
browser image into a product baseline.

References require three independent fresh-process captures with identical decoded
pixels. Regenerate one capture with:

```powershell
tools/capture-chrome.ps1 -InputPath tests/Research/circle-6x6.svg -OutputPath artifacts/research/circle.png -Width 6 -Height 6 -CacheDirectory artifacts/chrome
```

Do not replace a committed reference until the three-capture stability result,
hashes and metadata have been reviewed.

Each oracle entry records the pinned Chrome version, Chromium revision, extracted
executable SHA-256, capture recipe and device scale factor. These Windows-pinned
observations are research evidence only; they are not a canonical Linux baseline.

`tiger-16551.svg` is an external path-heavy research illustration from
Openclipart. Its manifest preserves the downloaded original hash, creator, source
page and CC0 evidence separately from the materialized SPDX-commented bytes used
for its reference. It is not an admitted fixture and must remain
`UnsupportedFeature` until the general path pipeline has independent exact
evidence.
