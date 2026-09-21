using System.Runtime.CompilerServices;

// The budget ledger has arithmetic invariants that cannot be established through
// the unimplemented public renderer. Keep this narrow test seam source-level.
[assembly: InternalsVisibleTo("Lokad.Svg.Tests")]
[assembly: InternalsVisibleTo("Lokad.Svg.VisualTests")]
[assembly: InternalsVisibleTo("Lokad.Svg.MemoryProbe")]
// Lets benchmarks measure internal discovery and integrity phases without duplicating synthetic font fixtures.
[assembly: InternalsVisibleTo("Lokad.Svg.Benchmarks")]
