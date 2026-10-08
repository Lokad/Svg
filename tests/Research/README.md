# Rasterization research probes

These are original CC0 calibration inputs, not supported-product fixtures. Each
probe has a pinned-Chrome reference in `references/` and records either a
categorized rejection or an admitted render with an explicit pixel difference.
The main manifest contains 65 probes: 64 exact and one rejection. Separate
mechanism banks retain their own denominators. `iching-library/manifest.json`
has 25 controls: 22 exact, two categorized rejections and one measured nested
affine-opacity difference against frozen references. The complete retained
I Ching document is measured separately in the online gap manifest.
`pattern-vector-content/manifest.json` retains eighteen controls: seventeen exact
and one explicit recursive-paint rejection. Its full 200×200 retained online
document is complete separately; source, options and reference remain frozen. Exact probes retain their original evidence and graduate to
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

`pattern-inheritance/` retains seventeen focused CC0 controls for the complete
Inkscape pattern: fifteen exact local-template/fractional-grid comparisons and two
explicit unit/external-template boundaries through six outputs. Three fresh healthy,
cleaned browser captures per case are byte-stable; existing assets stay frozen.
