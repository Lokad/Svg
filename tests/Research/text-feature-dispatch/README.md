# Shared text feature dispatch

Open review: shaping activation depends on marks/default-ignorables, while
canonical decomposition and selected font features have independent requirements.
These controls freeze the resulting gaps before changing that shared model.

| Control | Native authority | Current library outcome |
| --- | --- | --- |
| [Canonical substitution](ccmp.json) | `AV` matches literal `VV` through one `A → V` ccmp lookup. | Wrong ink: 320/330 differing pixels Windows/Linux; literal control exact. |
| [Contextual advance](context/capture.json) | A format-2 class context invokes one SinglePos X-advance action; removing the feature changes 500 pixels per host. | `UnsupportedFeature` on an ordinary ASCII run; the font without that lookup is exact. |
| [Canonical components](normalization/capture.json) | A missing precomposed `Á` maps through covered `A` and acute components, exactly matching the decomposed control. | `UnresolvedResource`; decomposed control exact. |
| [Canonical fallback](normalization-fallback/capture.json) | Adding a second family preserves the primary program's canonical output. | Acquires both programs and selects the wrong face: 643/575 differing pixels; decomposed control acquires only the primary and is exact. |

Each role retains three fresh healthy browser captures on each host. The four
small programs derive from the existing [CC0 PlainKern program](../kerning-precedence/LICENSE.txt),
preserving outlines, metrics and instructions. Independent source/reference
identities and pre-correction outcomes remain frozen. Equal images do not merge
these different lookup, normalization and font-demand responsibilities.

The [Noto reproduction](normalization-noto/capture.json) independently confirms
the canonical-components gap with the existing OFL program. Its exact derivative
is reproducible from that source; the bank retains no duplicate Noto program.
The [bounded font inventory](font-feature-inventory.json) records 46 distinct
programs and their selected Latin/default features. Some ordinary admitted text
uses programs with unsupported but irrelevant ccmp forms. This constrains a
blanket feature-activation change; the inventory is not pixel qualification.

Rebuild programs with fontTools 4.66.1 and the
[developer procedure](../../../tools/README.md#font-program-controls). References
are evidence for pending work, not passing renderer qualification.
