# Lokad.Svg

Lokad.Svg converts static SVG to PNG on .NET 10, on Windows and Linux, without
runtime package dependencies. This development preview implements a bounded SVG
subset rather than full browser conformance.

```csharp
using System;
using System.IO;
using System.Threading;
using Lokad.Svg;

ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(
    File.ReadAllBytes("input.svg"),
    SvgRenderOptions.Default,
    CancellationToken.None);
File.WriteAllBytes("output.png", png.ToArray());
```

The subset includes affine geometry, local instances, gradients/patterns,
clips/masks, embedded base64 PNGs, static CSS, markers, selected filters and
supplied-font text. Unsupported visible features report `SvgException`.
Rendering applies explicit memory/work limits and cancellation.

Outputs support owned memory, caller memory and writable streams, synchronously
or asynchronously. Text needs an explicit font catalog and font-program provider.
`SvgPreparedDocument.CreateAsync` prepares a document for one conversion; the host
owns font sources, resource acquisition and caching.

This package includes CHANGELOG.md, LICENSE.txt and THIRD_PARTY_NOTICES.txt.
The repository contains the detailed support matrix, contributor documentation
and test evidence.
