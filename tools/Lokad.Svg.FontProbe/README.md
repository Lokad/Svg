# Local font probe

`Lokad.Svg.FontProbe` is an opt-in local validation host for one caller-supplied
font-pack manifest and a directory containing its declared program files. It has
no built-in pack ID, location, network endpoint, credential, or platform
configuration discovery.

Run it only with explicit locations:

```powershell
dotnet run -c Release --project tools/Lokad.Svg.FontProbe -- --manifest <manifest.json> --pack-id <pack-id> --font-root <font-directory> --svg <input.svg> --output <output.png>
```

The manifest is read under 1 MiB. A selected program is read only when SVG
preparation requests it, and is capped at the smaller of its declared byte size
and 64 MiB before the library's separate 32 MiB selected-program cap applies.
The host writes PNG output through `SvgPreparedDocument`, so discovery,
verification, scene preparation, rasterization and encoding share its limits.
The supplied output path is created or truncated; the local caller owns cleanup
if a failed conversion leaves partial output.

Keep real manifests, program files, SVGs, output PNGs, hashes and any local
reports outside the repository or under `artifacts/private-fonts/`, which is
ignored. Console reports deliberately contain only outcome, SVG error category,
output size and selected-program request count; they omit paths, pack IDs,
hashes and exception text.
