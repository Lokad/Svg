# HTTP font packs

`SvgHttpFontPackResolver` is an explicit host-configured source. Ordinary SVG
conversion does not fetch resources. The resolver reads `manifest.json` beneath
a trusted HTTP(S) directory root, then downloads only selected manifest programs.
SVG URLs and font-family strings cannot select a network source.

The host supplies an HttpClient, acquisition policy and longer-lived
[memory budget](../src/Lokad.Svg/Memory/IMemoryBudget.cs). Construction performs no
I/O. Keep the client and budget alive through resolver I/O and all document/result
borrowers. Configure the client's handler to disable redirects or restrict them
to trusted destinations. Reported final response URIs outside the root reject;
the resolver cannot undo redirects already followed by the handler.

```csharp
await using var resolver = SvgHttpFontPackResolver.Create(
    "approved-pack", new Uri("https://fonts.example/approved-pack/"), httpClient,
    new SvgHttpFontAcquisitionOptions(
        TimeSpan.FromSeconds(30), 3, TimeSpan.FromMilliseconds(100),
        1_048_576, 33_554_432, 67_108_864), memoryBudget);
using var document = await SvgPreparedDocument.CreateAsync(
    svg, resolver, renderOptions, cancellationToken);
using SvgPngResult png = await document.ConvertToOwnedPngAsync(cancellationToken);
await destination.WriteAsync(png.Memory, cancellationToken);
```

The document uses `renderOptions.MemoryBudget` when supplied, otherwise the
resolver budget. The host must supply owners sharing one root; the library neither
infers ancestry nor copies a remaining balance. Keep that document/result owner
alive until borrowers finish.

Use `renderOptions.WithFontSelectionPolicy` for explicit generic mappings and
ordered fallback restrictions. Catalogs, selection and TrueType validation share
the existing supplied-font implementation. [Text support](SUPPORT_MATRIX.md#text-and-fonts)
still determines admitted faces, shaping and per-document bounds.

| Owner | Storage | Lifetime |
| --- | --- | --- |
| Resolver/cache | Shared acquisition, catalog and verified programs | Resolver closure plus completion of outstanding I/O and document borrowers. |
| Prepared document | Retained admission indices, scalar requirements, selected snapshot, scene and raster/encoder scratch | One conversion or disposal, after actual users finish. |
| Owned PNG result | Encoded PNG bytes under a sibling scope | Result disposal after readers/writes finish. |

Font-free shapes, hidden text and unused definitions do not initialize the
resolver or wait for another document's acquisition. Text preparation retains its
styles and dependency indices through acquisition and lowers the scene once.

Concurrent callers share catalog/program acquisition. Canceling a waiter ends
its wait; shared work keeps its own finite deadline covering headers, body and
retry delays. Successful catalogs remain for the resolver lifetime. Failed
attempts can recover on later calls; size/SHA-256 verification precedes successful
program admission. Responses and temporary storage are disposed on every exit.

Configured attempts retry HTTP 408/429/500/502/503/504 and transient connection,
name-resolution, premature-end or I/O failures. Cancellation, timeout, admission
refusal, invalid data and deterministic HTTP rejection do not trigger retries.
Diagnostics preserve acquisition stage, font selection and SVG source context
where available.

Idle program capacity is separate from host admission. Zero disables idle
retention. Active programs stay pinned and charged; they also retain document
face/byte bounds. Refused admission ends the operation without eviction, waiting
or fallback. Disposal never disposes the borrowed client or root budget.

Use `ConvertToOwnedPng`/`ConvertToOwnedPngAsync` for governed retained output.
Caller-memory and stream methods remain available. Governed bare-memory output
rejects because its charge could not follow the returned storage lifetime.
