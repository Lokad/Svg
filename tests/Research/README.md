# Rasterization research probes

These are original CC0 calibration inputs, not supported-product fixtures. Each
probe has a pinned-Chrome reference in `references/` and records either a
categorized rejection or an admitted render with an explicit pixel difference.
The current manifest contains 53 probes: 27 exact, 25 known-difference renders
and one rejection. Exact probes retain their original evidence and graduate to
the fixture bank with their original inputs, references and capture provenance.
Research records preserve reproducible evidence for geometry
and paint-server work without treating an inexact render as a product baseline.

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
