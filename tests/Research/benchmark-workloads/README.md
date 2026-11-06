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

[Screening](screening.json) against the qualified renderer finds exact icons and
wordmark on both hosts. The chart differs at seven pixels around the rounded
panel's lower-right corner, with maximum channel/alpha error 21/3. Benchmark
integration waits for the shared cause fix; sources and references remain frozen.

The [panel controls](panel-controls.json) separate that interaction:

| Control | Current result on both hosts | Responsibility |
| --- | --- | --- |
| panel-shadow | Seven differing pixels | Translated rounded source with nonzero blur |
| panel-source | Exact | Same rounded geometry without its filter |
| sharp-shadow | Exact | Same shadow with sharp source corners |
| origin-shadow | Exact | Same rounded shadow with the source at the origin |
| zero-shadow | Exact | Same translated rounded source with zero blur |

Each control retains three independent healthy captures per host. The finding
does not identify the responsible production stage. Inspect bitmap placement,
retained conic construction and filter sampling/alpha together; preserve these
positive boundaries and the original chart while correcting a shared cause.
