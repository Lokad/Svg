# Current text paint precision review

The [39 retained originals](../text-paint-bounds/precision-residuals.json) now have
35 exact inputs on both hosts and four unresolved witnesses. The
[current results](precision-current.csv), [before results](precision-before-stroke.csv)
and [qualification](stroke-drawing-qualification.json) preserve every source,
option, platform reference and exact difference. No reference or tolerance changed.

Stroke construction now forwards the existing fragment drawing map separately
from fitted glyph placement, consistently with fills. This closes the original
`spacing-paint-219-control`, `spacing-paint-479-candidate` and
`spacing-paint-479-control` inputs on both hosts/runtimes, including the 479 pair's
red/blue discontinuity and the Windows control's separate alpha difference.
Layout, shaping, ink maps, contour transfer and existing owners remain unchanged.
Three direct mechanisms cover linear/radial/pattern forwarding; three canonical
rendering regressions reuse the original sources and native PNGs. All six fail
before the fix against the previously qualified library.

[Fresh native verification](precision-native-verification.json) reproduces all 39
original targets in three healthy, profile-cleaned processes per host: 234 exact
comparisons. The original two-style regular/400 and Mono/700 font graph, Image
decode, transparent canvas, each row's scale and DPR1 remain. Both 1x and 1.5x
contexts are retained. Linux uses the existing scalar-selected CORE-MATH companions
for four inputs; original legacy references remain untouched. No new reference
files are needed.

| Remaining input suffix | Windows changed pixels | Linux changed pixels |
| --- | ---: | ---: |
| 224-control | 1 | 0 |
| 232-control | 0 | 1 |
| 430-candidate | 1 | 1 |
| 430-control | 1 | 1 |

All remaining differences affect color only. No previously exact result is lost;
Windows changes only the three closed inputs and Linux only the 479 pair.
The earlier 233 pair remains exact. Broader R48/R49/R60/R61, R103/R104 and decoration
work remains open; this closes the shared stroke-drawing cause and named witnesses.

Focused checks pass 175 mechanism/ownership and 572 native/provenance cases per
runtime/host. Full .NET 11 passes 47,548 core cases on each host and 46,696 Linux
rendering cases; Windows retains only its unchanged DejaVu reference conflict.
All 204 original outcomes/diagnostics and 168 rendered PNGs remain unchanged;
22 supplied-program contexts and 13 declared static diagnostics remain exact.
Installed-package checks also pass both runtimes on Windows/Linux from committed
implementation `3b609957f`; repeated runtime/symbol packages are byte-identical.
[Matching CI](https://github.com/Lokad/Svg/actions/runs/41773828741) passes the full
Ubuntu workflow. Windows passes all core/font-host tests and retains only the
unchanged DejaVu rendering conflict. Its later workflow steps are skipped; the
local installed-package checks above remain separate. The run is terminal/closed.
Timings in qualification are validation costs, not renderer benchmarks.

The 430 pair samples the same 9x9 texture on both hosts at coverage 130/255.
A separately compiled pinned-stage arithmetic control reproduces its coordinate,
fraction and color bits, including the current premultiplied bytes `[27,0,103,130]`.
This checks downstream arithmetic for the supplied matrix; it does not establish
that the browser uses that matrix. The opacity group carries the same difference.

Next: isolate fragment/reference/local/picture matrix preparation with a small
independent transform control, then the two platform-specific channel witnesses.
Preserve the original targets and half-byte boundaries. The
[qualification record](stroke-drawing-qualification.json) retains the exact bits,
operation order and source pin; no rounding adjustment is justified by this trace.
