# Lokad.Svg

Lokad.Svg is a forthcoming pure-managed .NET SVG-to-PNG renderer. It is being
built in staged, explicitly supported subsets; it does not yet render SVG.

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
