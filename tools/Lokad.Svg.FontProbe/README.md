# Local font probe

`Lokad.Svg.FontProbe` is an opt-in local validation host for one caller-supplied
font-pack manifest and a directory containing its declared program files. It has
no built-in pack ID, location, network endpoint, credential, or platform
configuration discovery.

Run it only with explicit locations:

```powershell
dotnet run -c Release --framework net11.0 --project tools/Lokad.Svg.FontProbe -- --manifest <manifest.json> --pack-id <pack-id> --font-root <font-directory> --svg <input.svg> --output <output.png>
```

Alternatively, provide an explicit HTTP(S) source root. The host requests
`<source-root>/<pack-id>/manifest.json` and only requests declared selected
program paths below that pack root:

```powershell
dotnet run -c Release --framework net11.0 --project tools/Lokad.Svg.FontProbe -- --source-uri <https://source-root/> --pack-id <pack-id> --svg <input.svg> --output <output.png>
```

The source URI must be absolute HTTP(S), end at a directory, and have no query or
fragment. The host has no credential, endpoint or platform discovery; configure
authentication outside the tracked command and source code.
Pack IDs must be single relative path segments. Literal or URI-escaped dot
segments and separators are rejected before any HTTP request.

The command uses manifest aliases and fallback families. Applications that need
generic-family mappings supply `SvgFontSelectionPolicy` explicitly when creating
a font snapshot or prepared document. The probe does not discover mappings or
read an adjacent host-policy file automatically. Family lists retain authored
order; coverage-based selection uses only supplied programs.
Use the probe built with the same Lokad.Svg library as your application.
Programs follow the renderer's [bounded text/font subset](../../docs/SUPPORT_MATRIX.md#text-and-fonts),
including admitted pair lookup/GDEF filters; actual mark positioning remains unsupported.
Catalog aliases do not enable another program's source-font raster policies.
Font-selected unfitted ranges retain ideal outline positions while honoring
material scanner controls; logical text metrics stay independent of pixel fitting.

For either source, use `--svg -` to read raw SVG bytes from standard input. This
path does not seek, decode, strip a BOM or pre-read a length; it stops before
retaining data above the normal SVG input limit.

The manifest is read under 1 MiB. A selected program is read only when SVG
preparation requests it, and is capped at the smaller of its declared byte size
and 64 MiB before the library's separate 32 MiB distinct-program cap applies.
Coverage fallback acquires later authored families and allowed catalog fallbacks
only when an admitted scalar needs them; examined rejected programs also count
against that preparation bound. Required missing/corrupt bytes fail explicitly.
HTTP requests retry transient transport failures at most three times. Successful
HTTP programs coalesce concurrent reads and remain in a shared cache capped at
32 MiB; a program that cannot fit remains usable for its immediate consumer but
is not retained by that cache.
The focused `Lokad.Svg.FontProbe.Tests` project covers concurrent coalescing/cache
reuse, cache-cap bypass, transient retry and cancellation recovery with synthetic
transport, plus exact nonseekable-stream copies and growth-time input denial.
The host writes PNG output through `SvgPreparedDocument`, so discovery,
verification, scene preparation, rasterization and encoding share its limits.
The supplied output path is created or truncated; the local caller owns cleanup
if a failed conversion leaves partial output.

Keep real manifests, program files, SVGs, output PNGs, hashes and any local
reports outside the repository or under `artifacts/private-fonts/`, which is
ignored. Console reports deliberately contain only outcome, SVG error category,
output size and selected-program request count; they omit paths, pack IDs,
hashes and exception text.

Public regular, bold, real italic and intentional fallback requests qualify the
standalone renderer through this same host, manifest and provider interfaces.
Redistributable assets and exact platform references remain offline test data;
no private configuration is needed by ordinary builds or CI. The four original
private deployed-manifest requests are a separate pending production-adoption
gate. Public success does not establish compatibility with that deployment.

[Public qualification](../../tests/Research/public-font-qualification/README.md)
passes all four requests on Windows/Linux through this executable, with independent
metrics and exact platform references. Run the offline checks with
`dotnet test tests/Lokad.Svg.VisualTests -c Release --filter FullyQualifiedName~SvgPublicFontQualificationTests`.
The test project builds this existing host and owns its temporary public font root;
no deployment configuration or source discovery is involved.
