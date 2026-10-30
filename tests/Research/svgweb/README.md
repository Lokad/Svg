# Hosted SVGWeb review

[Originals](originals.json) accounts for all 204 direct `.svg` entries in the
[hosted directory](https://dev.w3.org/SVG/tools/svgweb/samples/svg-files/): URL,
original byte hash/size, root declarations, lexical font/resource use and initial
library outcome. SVG bodies are fetched into bounded memory and discarded; this
directory contains metadata, not the hosted collection. Ordinary tests are offline.

The first original-byte pass uses one `SvgConverter.ConvertToPng` call with default
options, transparent output and no supplied fonts/resources. All 204 were attempted
on Windows and Linux against the library identity recorded in the ledger: 136
rendered and 68 rejected on each host, with identical outcome categories. The
combined diagnostic pass took 62.21 s, including retrieval and process startup.
These are initial outcomes, **not 136 accepted browser matches** or a throughput
benchmark. Each original still needs rendering qualification.

| First blocking area | Originals | Required review |
| --- | ---: | --- |
| Gradient-stop `id` attributes | 13 | Admit valid identity metadata without changing stop values, inheritance or resource lookup. |
| Fixed units in stroke offsets | 3 | Reuse a consistent length model; isolate units from dash geometry and raster rounding. |
| Other gradient declarations | 5 | Partial radial vectors, percentage coordinates, invalid attributes and invalid units need separate valid/invalid controls. |
| Required fonts | 9 | Supply declared programs explicitly; distinguish SVG-font previews from font-free geometry. |
| Root events | 10 | Static-state rendering and an explicit interaction limitation; no script execution in conversion. |
| Child-content rejection | 11 | Inspect the actual parent/state; the reported child is not necessarily the shared cause. |
| External raster content | 3 | Bind each actual resource and host acquisition/materialization separately. |
| Remaining boundaries | 14 | CSS, clip composition, authoring attributes, XML declarations and duplicate IDs retain individual diagnostics in the ledger. |

Counts classify the first blocker only. Removing one can reveal another. Do not
treat current unsupported behavior, parsing success, machine-font substitution or
an unexplained image difference as closure. Font declarations are lexical inventory,
not resolved requests; external-reference uses distinguish navigation from image,
CSS or media dependencies without fetching them implicitly.

Original browser exploration uses the pinned image/screenshot path independently
on each platform. Root sizing must be matched explicitly: a viewBox-only image's
intrinsic CSS size can differ from the library's default 300×150 viewport. Preserve
original bytes/options and label any viewport materialization. Scripts are inactive
in image documents; animation still needs a declared frozen state. Ambient fonts
and unresolved resources cannot qualify a target.

The named image-viewport mode has three fresh healthy captures per platform for
12 calibration contexts, including the three original samples `aa.svg`,
`accessible.svg` and `android.svg`, Blob transport and opaque backgrounds. The
three originals are exact at the default 300×150 logical viewport. Fractional,
percentage and CSS-size controls retain their 26/90/90-pixel differences; they
remain unqualified for renderer acceptance. A separate canvas exploration makes
the absolute fractional control exact but changes viewBox-only sizing, so it
cannot substitute for the screenshot recipe. See the
[capture model record](capture-model.json) for identities and measurements.

Group confirmed differences by shared mechanism and compose small original fixtures
with their original-to-fixture mapping, licenses and independent native references.
Keep distinct interactions and failing-before evidence. Admission still requires
three fresh healthy captures per platform and exact established comparison rules;
single exploratory screenshots are not regression references. Recheck all originals
after shared fixes. [Tool procedure](../../../tools/README.md#hosted-svgweb-inventory)
reproduces the metadata inventory without retaining SVG files.
