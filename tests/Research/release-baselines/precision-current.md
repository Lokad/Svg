# Current text paint precision review

The [39 retained originals](../text-paint-bounds/precision-residuals.json) were
replayed at `e5b42ff6e13af92ff0943b1c34b336fe28043e12`. Production is unchanged
since the text-clip milestone. The .NET 11 library is stamped `ccb2c5bdf` with
SHA-256 `e29b2720f85f4395db10580db550eaac75925d1eba6522d4d45110c9746e5914`.
The [78 platform results](precision-current.csv) bind every source, selected
reference, current PNG and exact difference. No source, reference or tolerance changed.

Replay uses the existing `r99-fonts` catalog, pinned Noto Sans regular/Mono bold,
each original's scale, transparent output and one public `ConvertToPng` call.
Reference PNG and decoded pixel hashes are checked before comparison. Linux uses
the existing scalar-selected CORE-MATH companions for four inputs; their source
and native-input hashes must match. Original legacy references remain untouched.
The existing comparer retains exact canonical RGBA, including every alpha byte.

32 inputs match both hosts. Seven remain inexact, versus eight in the preceding
shared-paint review. Both `spacing-paint-233` inputs now match; the previously
exact `spacing-paint-479-candidate` has a newly observed difference.

| Input suffix | Windows changed pixels | Linux changed pixels |
| --- | ---: | ---: |
| 219-control | 1 | 0 |
| 224-control | 1 | 0 |
| 232-control | 0 | 1 |
| 430-candidate | 1 | 1 |
| 430-control | 1 | 1 |
| 479-candidate | 4 | 4 |
| 479-control | 5 | 4 |

The 479 pair differs at x=107, y=38–41: the reference is blue, current output red,
with identical alpha at those pixels on both hosts. The ungrouped Windows control
also retains one separate alpha/color difference at (137,27). Other residuals are
single-pixel color differences. These observations locate paint/sampling work;
they do not establish a text-layout cause.

This is a current rendering replay against frozen independent targets, not new
browser qualification or reference admission. It does not close the broader
R48/R49/R60/R61, R103/R104 or decoration review. The preceding 960-input audit and
all original observations remain intact. Replay took 2.26 s Windows and 5.17 s
Linux, including process startup; these functional costs are not benchmarks.

Next: reproduce the seven witnesses and matched controls with three healthy native
captures per host, preserving the original two-style font graph and canvas-scale
recipe. Trace texture preparation, drawing maps, repeat addresses and rounding
through shared stages; retain the 479 pair's alpha and geometry while isolating
its color discontinuity. Reuse existing numerical mechanisms and canonical images
before adding any context-specific rule or API matrix.
