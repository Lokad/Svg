# Lokad.Svg

Lokad.Svg is a pure-managed .NET SVG-to-PNG renderer being built in staged,
explicitly supported subsets. The current subset renders explicitly sized empty
SVGs and integer-aligned `rect` layers with `none`, `transparent`, `red`, `blue`,
`#RGB` or `#RRGGBB` fill and `fill-opacity`, composited in document order.
All other visible behavior fails explicitly rather than being silently dropped.

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
