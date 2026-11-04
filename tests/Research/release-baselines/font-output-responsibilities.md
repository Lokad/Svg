# Supplied-font output responsibilities

All 37 inputs retain one canonical conversion, source immutability and exact
independent Windows/Linux references. Sources, font programs, options, inclusion
boundaries and frozen references are unchanged. Curved/CSS cases explicitly require
`rendererOutcome=exact`; obsolete conditional acceptance no longer skips pixels.

Seven representative inputs retain the complete twelve-form API contract during
routine development. Selection follows execution responsibilities, not equal images:

| Bank / input | Additional API interaction |
| --- | --- |
| supplied-font-documents / commons-svg-translate-textpath-512x256-transparent | Supplied Mono Bold, explicit viewport and complete positioned document. |
| supplied-font-text-paths / forward-xlink | Deferred reference resolution with an inferred viewport. |
| supplied-font-text-paths / midpoint-outside | Font discovery followed by suppressed glyph output. |
| curved-font-documents / two-path-runs | Multiple separately queued path runs. |
| curved-font-documents / composite-on-path | Composite glyph source retained through path preparation. |
| static-css-document / span-paint | Late stylesheet discovery and inherited span paint. |
| static-css-document / translated-pattern-rect | Font-free preparation and retained pattern resources. |

Each matrix checks returned memory, caller memory and streams, synchronously and
asynchronously, raw and prepared. Every prepared form creates and disposes its own
document/font lease; reused destination memory is poisoned before conversion.
The other 30 matrices remain individual `Layer=Contract, Category=Research` cases.
Full qualification runs all 37 matrices plus all 37 rendering cases. There is no
batched rendering assertion or reduction in full qualification coverage.

Dedicated font-selection, layout, denial, cancellation, cleanup and retry tests
remain unchanged in the three test classes. Owned-result charging and lifetime
remain in `OpenTypeFontTests.SharedMemory`; the eight-input, 96-case conversion
matrix separately covers destination ownership and byte equivalence. These duties
do not depend on repeating every rendering fixture through every API.

After a Release build, use these filters in `Lokad.Svg.VisualTests`:

| Check | Filter |
| --- | --- |
| Native rendering | `(FullyQualifiedName~SvgSuppliedFontDocumentTests\|FullyQualifiedName~SvgSuppliedFontTextPathTests\|FullyQualifiedName~SvgCurvedFontDocumentTests)&Layer=Rendering` |
| Representative API forms | `FullyQualifiedName~Representative_font_documents_preserve_twelve_output_forms` |
| Affected integration | `FullyQualifiedName~SvgSuppliedFontDocumentTests\|FullyQualifiedName~SvgSuppliedFontTextPathTests\|FullyQualifiedName~SvgCurvedFontDocumentTests` |

The integration selection includes all extended matrices and the existing mechanism
and ownership cases. Add `SvgConversionApiContractTests` and the governed-output
contracts in the core project when changing the shared conversion/preparation path.
The [measurement record](font-output-qualification.json) binds local validation
costs to the same packaged renderer before and after; it is not a renderer benchmark.

Three fresh .NET 11 processes per host measure routine validation medians, including
startup/discovery/TRX and excluding build:

| Host | Mixed fixtures before | Rendering + representative contracts |
| --- | ---: | ---: |
| Windows | 15.02 s | 13.92 s |
| Linux | 16.03 s | 13.99 s |

Canonical rendering alone takes 11.15/11.56 s; full 74-case validation takes
15.47/15.59 s (Windows/Linux, one observation each). Full validation adds 37 canonical
conversions to the original 444-form product; routine work performs 121 conversions.
All 155 affected visual and 119 API/ownership cases pass on both hosts under
.NET 11 and .NET 10. Builds are warning-free. Renderer code and package bytes are
unchanged; this split closes these four banks, not the remaining suite-wide audit.
