# Governed host API qualification

The HTTP resolver, single preparation, shared admission and owned PNG APIs already
exist in development. This qualification extends the existing installed consumer
and adoption guidance; it adds no resolver, preparation pipeline, memory model or
public API. [HTTP font guidance](../../../docs/HTTP_FONTS.md) owns the current usage
and cache/document/result lifetime explanation.

| Responsibility | Evidence |
| --- | --- |
| Construction and font-free independence | Controlled installed handler observes zero requests at construction/cold conversion and independent font-free work while another catalog request stalls. Existing source checks also cover warm/failed acquisition, hidden text and unused definitions. |
| Selection, reuse and waiter cancellation | Installed manifest includes an unused face; the handler rejects its acquisition. A canceled catalog waiter leaves a survivor usable. Concurrent warm preparations reuse the same two catalog/program requests and match the explicit-provider PNG exactly. |
| Shared root and refusal | Separate cache/operation owners share one test root. Acquisition metadata and rendering refusals report `LimitExceeded`, release failed operations and do not retry transport or change font selection. Later operations recover. |
| Shutdown borrowers | Two prepared documents survive resolver shutdown. Disposing one preserves the other; its conversion keeps the PNG's sibling charge until result disposal. The borrowed HttpClient remains usable. |
| Shutdown I/O | A canceled acquisition deliberately holds its cleanup gate. `DisposeAsync` remains incomplete and storage stays charged until actual I/O cleanup finishes; preparation then cancels and the borrowed allowance is reusable. |
| Output and cancellation | One caller-memory conversion checks exact bytes and its entire guard. An async-only stream proves real backpressure, exact output, cancellation cleanup and borrowed destination ownership. An owned PNG remains charged across successful and canceled delayed writes after document/resolver release. The supplied operation parent stays alive. |
| Single preparation and detailed HTTP policy | The existing [prepared HTTP contracts](../../Lokad.Svg.Tests/OpenTypeFontTests.HttpPrepared.cs) observe one preparation and retained output ownership. [Resolver contracts](../../Lokad.Svg.Tests/OpenTypeFontTests.HttpResolver.cs) retain deadline/body, integrity, recovery, selection, transport and structured-failure responsibilities. No Cartesian package matrix repeats these mechanisms. |

The source selection contains 37 HTTP checks, all passing on Windows/Linux and
both supported runtimes in the shared-stage qualification. The installed consumer
uses the public package alone; compilation cannot depend on internal test access.
Its three concrete flows cover cached lifecycle/output, acquisition refusal and
shutdown during acquisition. Existing non-HTTP package cases remain unchanged.

The local candidate is `0.4.0-dev.1`, with library source stamped `544b74174` and
the adoption documentation from this change. Its
[identity record](host-api-package.json) binds nupkg, symbols and both library
assets. The normal package verifier checks required files, symbols, README links,
absence of runtime dependencies/native assets/fixtures, and isolated package restore.
Windows restores/builds/runs both targets. Local Linux runs deploy those portable
installed-consumer binaries and verify the library bytes against each package asset;
matching CI independently restores/builds/runs the consumer on Linux as well.

The local Windows verification (isolated restore, both builds and both executions)
took 6.90 s. Linux deployed-consumer executions took 6.93 s on .NET 11 and 6.88 s
on .NET 10, including WSL/process startup. These are single functional-validation
observations, not renderer throughput or a before/after speed comparison.

Source implementation, candidate qualification and publication are separate.
This work performs no NuGet publication and does not change the published release.
Quiet-machine benchmarking remains outside this functional qualification.
