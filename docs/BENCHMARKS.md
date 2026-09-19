# Local benchmark baseline

This is the initial local baseline for the current supported subset. It is a
comparison point for follow-up measurements on the same environment, not a
performance gate or a claim about representative SVG workloads. The `ShortRun`
job has three measured iterations, so its confidence intervals remain broad.

## Reproduction

```powershell
dotnet run -c Release --project benchmarks/Lokad.Svg.Benchmarks -- -j Short -f "*"
```

BenchmarkDotNet writes the complete raw log, CSV and HTML report under its
ignored `BenchmarkDotNet.Artifacts/` directory. Preserve a result separately
when comparing revisions; do not infer a regression from one run on a different
machine or runtime.

## Baseline captured 2026-09-19

- BenchmarkDotNet 0.14.0; .NET SDK 10.0.401; .NET 10.0.12 x64 RyuJIT.
- Windows 10 22H2 (10.0.19045.6456), Intel Xeon W-2223 3.60 GHz, four physical
  / eight logical cores, concurrent workstation GC.
- `ShortRun`: one launch, three warmups and three measured iterations.
- Each input is one explicitly sized SVG with a full red rectangle. Input setup
  and caller destination allocation occur outside the timed method. The stream
  methods reset the same preallocated caller-owned `MemoryStream` before every
  conversion.

| API form | 1×1 | 64×64 | 512×512 |
| --- | ---: | ---: | ---: |
| Owned memory | 14.86 μs / 5.89 KB | 122.72 μs / 22.59 KB | 3,455.11 μs / 1,046.26 KB |
| Caller memory | 14.25 μs / 5.72 KB | 122.10 μs / 22.25 KB | 3,537.82 μs / 1,040.03 KB |
| Stream | 12.18 μs / 5.74 KB | 113.20 μs / 22.04 KB | 3,758.53 μs / 1,033.98 KB |
| Async stream | 35.52 μs / 6.51 KB | 161.70 μs / 22.86 KB | 3,888.44 μs / 1,037.85 KB |

Each cell is mean time followed by BenchmarkDotNet managed allocation per
conversion. It is allocation traffic, not retained memory or `RenderBudget`
peak. The probe scenarios described in the root README cover the separate
process/outcome evidence; they do not make an OS working-set measurement into a
renderer memory limit.
