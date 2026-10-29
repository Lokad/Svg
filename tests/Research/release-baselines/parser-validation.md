# Validation after fuel removal

Parser fuel is removed from parsing, font preparation, geometry construction and
PNG decoding. `ParserWorkCounter`, its arguments and retained parser totals are
gone. Cancellation checks remain in actual traversal loops. Structural limits,
checked storage sizes and numerical domains remain independent contracts.
Raster quota plumbing, remaining-fuel algorithm choices and MaximumWorkUnits are
also removed. The private memory-cap migration remains separate; this record does
not close shared memory governance.

| Removed expectation | Retained responsibility |
| --- | --- |
| Exhausting a parser counter at each synthetic unit | Direct parsing/geometry/shaping vectors, complete-input syntax and structural boundaries, exact numerical and native-image assertions. |
| Repeating a successful operation with a slightly smaller counter | Actual allocation refusal, growth peaks, partial-owner cleanup, borrowed-input preservation, cancellation and immediate retry in the same contract classes. |
| Synthetic work totals used as complexity evidence | Allocation-growth checks for clip siblings, template chains and shared text/filter layout. These measure managed allocation only. CPU scaling remains a benchmark responsibility. |
| Font bytecode backward jumps stopped by fuel | [Interpreter cancellation](../../Lokad.Svg.Tests/TrueTypeHintingTests.cs), with the original token, plus independent call-depth, program-size, stack and numerical checks. |
| Fuel refusal standing in for a transfer failure | Real memory refusal in [authored-path replacement](../../Lokad.Svg.Tests/SvgAuthoredPathTests.cs) and [detached glyph transfer](../../Lokad.Svg.Tests/OpenTypeFontTests.cs). |
| A low work allowance proving eager prepared-scene lowering | A valid font-free SVG with an unresolved paint reference fails during [prepared-document creation](../../Lokad.Svg.Tests/SvgFontProgramSourceTests.cs); font discovery alone succeeds. |

The general [API matrix](../../Lokad.Svg.Tests/SvgConversionApiContractTests.cs)
continues to cover distinct destinations and execution paths. Prepared and direct
glyph coverage retain separate allocation paths. Numerical banks, authored inputs,
native references, platform differences, tolerances and licenses are unchanged.
Counter-only tests are removed; mixed tests retain their independent assertions.

[Symbol sealing](../../../src/Lokad.Svg/Parsing/SvgSymbolContent.cs) stages its
closing node in the result array. Cancellation after copied nodes 1, 4 and 5 leaves
the original builder and its charge unchanged, then permits a successful retry.
The [regression](../../Lokad.Svg.Tests/SvgSymbolContentTests.cs) previously warmed
that mutation through an obsolete fuel-refusal call. Removing the warmup exposed
the retained-capacity change; the implementation now avoids it.

Source review starts at `596125e0f` for parser fuel and `09d485074` for raster
fuel: use `git show` on the relevant test file to inspect prior expectations.
This map and the source tests are the
durable evidence; local test receipts are generated output. Qualification commands
and development/milestone selections remain in [the test guide](../../README.md).

The raster migration retains actual point/piece/edge counts, array bounds,
subdivision depth/error, sampling and rounding. Analytic row-event memory refusal,
growth peaks and cancellation remain separate from removed operation quotas.
Blur tests compare pixels, source immutability, per-axis cancellation and owner
cleanup without synthetic return totals. The analytic clipper's former quota-only
binary-search test is covered numerically by the independent
`near-quadratic-binary-root-witness` row in
[the native piece bank](../../Lokad.Svg.Tests/TestData/analytic-clipped-pieces.json).
The memory probe's pattern refusal now tests byte admission instead of fuel.

The fallback glyph path prepares its subdivision points without a fuel-dependent
algorithm choice. Its prepared-storage refusal, coverage-entry cancellation and
exact pixels are separate checks in
[the budget contracts](../../Lokad.Svg.Tests/RenderBudgetTests.cs).
Cached native counts retain direct assertions; canceled uncached count/raw-control
preparations preserve their caches and can retry. Borrowed bitmap-curve construction
now refuses actual scratch storage before checking unchanged source curves and
exact retry pixels. Mixed clipping, mask, filter and shader tests retain real
memory refusal, cancellation and cleanup while their quota-only branches are gone.
The ligature lifecycle tests no longer repeat the same post-quota call 120 times;
they exercise the longer input once with a live storage charge, then verify release.
