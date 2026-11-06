# Text anchors and chunk ownership

Four original CC0 [controls](capture.json) retain three agreeing healthy browser
captures per host. Windows and Linux references remain separate because their
supplied-font pixels differ. The [canonical rendering tests](../../Lokad.Svg.VisualTests/SvgTextAnchorReferenceTests.cs)
use the unchanged authored input and the pinned [Noto Sans program](../supplied-font-labels/font-pack.json).
Native capture binds that same program through one embedded `@font-face`; its
input hash is recorded separately. The font retains its [OFL](../supplied-font-labels/fonts/OFL.txt).

- `inherited` protects root/group inheritance, presentation attributes, stylesheet
  specificity, inline priority, `!important` and global keywords.
- `chunks` protects child and ancestor absolute positions, the first character's
  anchor, and anchor-only changes within one continuous shaping chunk.
- `effects` protects fractional placement, fitting, filter offset, group opacity
  and symbol replay together. Its Windows stretching discrepancy was resolved
  through shared initial-point scaling, without changing the reference.
- `path` protects group inheritance over straight and curved path text.

[Mechanisms](../../Lokad.Svg.Tests/SvgTextAnchorTests.cs) exercise syntax, cascade
and span compaction. Four layout cases and two [contracts](../../Lokad.Svg.Tests/OpenTypeFontTests.TextAnchor.cs)
protect chunk coordinates, the additional owned-position array, refusal/refunds,
retry, validation before font acquisition and prepared replay. These contracts
are representative; the rendering bank does not repeat the API matrix.

[Independent native point vectors](../windows-font-unit-scaling/expanded-points.json)
separate the Windows strike arithmetic from rendering consumers. The
[milestone record](../svgweb/text-anchor-layout.json) retains failing-before
evidence and qualification results. The path control inherits its CSS from groups;
the separate [text-path style controls](../text-path-styles/README.md) protect
path-local declarations, their cascade and mixed direct/path ranges.
