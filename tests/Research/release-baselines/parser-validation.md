# Parser validation responsibilities

Parser fuel is removed from parsing, font preparation, geometry construction and
PNG decoding. `ParserWorkCounter`, its arguments and retained parser totals are
gone. Cancellation checks remain in actual traversal loops. Structural limits,
checked storage sizes and numerical domains remain independent contracts.
Raster work quotas and the private memory-cap migration are separate remaining
changes; this record does not close shared memory governance.

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

Source review starts at `596125e0f`: use `git show` on the relevant test file to
inspect the prior counter expectations. This map and the source tests are the
durable evidence; local test receipts are generated output. Qualification commands
and development/milestone selections remain in [the test guide](../../README.md).
