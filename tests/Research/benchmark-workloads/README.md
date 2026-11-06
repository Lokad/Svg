# Authored benchmark workload candidates

The [input manifest](manifest.json) freezes three original CC0 SVGs before browser
capture and renderer screening: a 512×384 icon sheet, 768×192 wordmark and
1024×640 chart. They exercise application-sized paths, instances, supplied labels,
gradients/patterns, masks/clips, shadows and an authored embedded PNG. Font programs
and their notices stay in the [upstream collection](../upstream-static-documents/README.md).
These authored workloads have separate membership from that collection's document gate.

[Capture metadata](capture.json) binds each source and declared font transport to
three fresh, healthy, stable Chrome processes per platform. Windows/Linux targets
remain independent. Absent-font controls change every workload; substituting the
regular program for real italic also changes the wordmark on both hosts. Font
bindings are appended in memory; source drawing nodes and attributes stay unchanged.

[Sampling-guard qualification](../release-baselines/shadow-sampling-guard.json)
closes the initial chart discrepancy by retaining the linear shadow transform's
one-pixel input guard. Both runtime assets match all three workloads and twelve
controls on Windows/Linux. The [initial screening](screening.json), original
sources and independent references remain frozen.

The [panel controls](panel-controls.json) separate that interaction:

| Control | Current result on both hosts | Responsibility |
| --- | --- | --- |
| panel-shadow | Exact | Translated rounded source with nonzero blur |
| panel-source | Exact | Same rounded geometry without its filter |
| sharp-shadow | Exact | Same shadow with sharp source corners |
| origin-shadow | Exact | Same rounded shadow with the source at the origin |
| zero-shadow | Exact | Same translated rounded source with zero blur |

Each control retains three independent healthy captures per host. Direct demand
mechanisms and one representative ownership contract complement canonical native
rendering; expanded graphs retain their separate nearest-offset behavior.

[Stage diagnostics](stage-controls.json) retain the failing-before transparent
shadow and independent blur/expanded-graph boundaries. Fused and expanded native
shadows differ at seven pixels; both now match their own targets. Three selected
diagnostics have native stability triples; four others remain exploratory.

The [color-materialization control](color-materialization.json) remains open:
an identity color-matrix prefix exposes 110 differing pixels on both hosts and
both runtime assets. Its independent targets have healthy stability triples.
Broader filter closure and benchmark integration wait for this separate finding.
