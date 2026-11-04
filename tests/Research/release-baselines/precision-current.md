# Current text paint precision review

Of the [39 retained originals](../text-paint-bounds/precision-residuals.json),
37 now match both hosts exactly. Two platform-specific color witnesses remain:

| Input suffix | Windows changed pixels | Linux changed pixels |
| --- | ---: | ---: |
| 224-control | 1 | 0 |
| 232-control | 0 | 1 |

[Current results](precision-current.csv), [before results](precision-before-fragment.csv)
and [qualification](fragment-drawing-qualification.json) preserve every original
source, option, font and platform target. No reference or tolerance changed.
The five closed stroke inputs are 219-control, the 479 pair and the 430 pair.
Broader node/anchor/phase, shader and decoration work remains open.

Text paints retain their enclosing reference, physical font scale and fragment
transform separately. Canvas preparation appends those stages before capture
placement; shader-local preparation keeps the inverse fragment stage. Linear,
radial and pattern fills/strokes share this value. Logical layout, shaping,
fitted glyphs, contour ownership, sampling and final rounding retain their paths.
The extra 56 retained bytes are admitted before contour allocation and transfer
with the existing owner; refusal, cleanup and retry remain one contract.

[Three independent vector controls](../text-paint-bounds/fragment-stages.json)
distinguish flattened, separate-fragment and physical-font canvas stages without
fonts. Each retains three healthy stable native captures per host: 18 comparisons.
The original [234 native comparisons](precision-native-verification.json) remain
valid for the unchanged original targets and two-style font graph. The 430 pair
fails before the shared-stage correction; only its two PNGs change on each host.
One direct numerical mechanism protects the half-byte boundary. Five original
stroke images and the three vector controls each use one canonical conversion.

Focused checks pass 177 mechanism/ownership and 577 rendering/provenance cases on
both runtimes and hosts. Full .NET 11 passes all 47,549 core cases per host and
46,701 Linux rendering cases. Windows retains only its unchanged DejaVu conflict.
All previous test identities remain. All 204 default outcomes/diagnostics and 168
rendered PNGs are unchanged; 22 supplied-font contexts and 13 static diagnostics
remain exact. Package and matching committed-head CI qualification are separate.
Timings in qualification are functional validation costs, not renderer benchmarks.

Next: isolate the two remaining object-box/channel boundaries with direct
independent controls, preserving source layout, font profiles and exact targets.
