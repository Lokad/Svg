using System.Runtime.CompilerServices;

// Internal validation and probes observe ownership and numerical invariants
// without adding public diagnostic APIs. Keep these test seams source-level.
[assembly: InternalsVisibleTo("Lokad.Svg.Tests")]
[assembly: InternalsVisibleTo("Lokad.Svg.VisualTests")]
[assembly: InternalsVisibleTo("Lokad.Svg.MemoryProbe")]
// Lets benchmarks measure internal discovery and integrity phases without duplicating synthetic font fixtures.
[assembly: InternalsVisibleTo("Lokad.Svg.Benchmarks")]
