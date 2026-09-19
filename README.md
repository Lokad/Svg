# Lokad.Svg

Lokad.Svg is a pure-managed .NET SVG-to-PNG renderer being built in staged,
explicitly supported subsets. The current subset renders explicitly sized empty
SVGs and integer-aligned `rect` layers with bounded root/group/link structure,
inherited paint/visibility/display, source-over composition, caller-provided
backgrounds, and managed PNG output to owned memory, caller memory or a writable
stream. It deliberately rejects broad CSS stylesheets,
gradients, images, filters and text; see the support matrix for the exact accepted
syntax, including its deliberately narrow root-level rectangle stylesheet selectors.
The current prototype also has documented accepted-input contract gaps, so rejected
syntax must not be inferred solely from the absence of support claims.

See [PLAN.md](PLAN.md) for the product contract and implementation roadmap, and
[the support matrix](docs/SUPPORT_MATRIX.md) for the current status.

## Development

```powershell
dotnet restore --locked-mode
dotnet build -c Release
dotnet test -c Release
dotnet pack src/Lokad.Svg/Lokad.Svg.csproj -c Release
```

The package will have no runtime package dependencies. Tests, Chrome tooling and
benchmarks are development-only projects.

To smoke-test the packed package from a clean consumer directory:

```powershell
dotnet new console --output $env:TEMP/lokad-svg-smoke
dotnet add $env:TEMP/lokad-svg-smoke package Lokad.Svg --version 0.1.0-preview.1 --source artifacts/packages
dotnet build $env:TEMP/lokad-svg-smoke -c Release
```
