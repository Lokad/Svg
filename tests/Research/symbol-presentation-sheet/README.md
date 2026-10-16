# Symbol container presentation

One authored CC0 sprite sheet and two controls demonstrate a live G2/G7 gap:
the qualified `833d5832` baseline rejects `display` and `visibility` on symbols.
Sources/options were declared before implementation. Each frozen Windows/Linux
reference has three identical healthy fresh-process captures with cleaned profiles,
using pinned Chrome 153.0.8010.52 and existing capture recipe 3.

The display control distinguishes attribute, inline `!important` and stylesheet
`display:none`, a displayed positive control and a hidden use. Pinned Chrome
suppresses instances of symbols with authored `display:none`; marker definitions
continue to ignore display. This measured symbol behavior differs from the
[SVG 2 direct-symbol-instance rule](https://www.w3.org/TR/SVG/struct.html#SymbolElement).
The initial native discriminator is retained in the ignored
[study](../../../artifacts/symbol-presentation-20261016/initial-native-discriminator/).

The visibility control preserves root inheritance, visible child/group overrides,
a visible symbol overriding a hidden use, an explicitly visible leaf within an
otherwise unstyled symbol referenced by a hidden use, and a direct child alias
that excludes its symbol ancestor's visibility. That last visible-leaf case
requires retaining source presentation instead of flattening away the override.
Both mechanisms reuse the existing bounded retained-content path and ordinary
presentation validation; no separate renderer or stroker is introduced.

The whole document and both controls are exact on Windows/Linux. Full local
qualification passes **8,142 tests/platform**, **104s Windows / 377s Linux** for
build plus complete tests. The existing denial/cancellation/cleanup/retry check
now includes this whole sheet; invalid display/visibility values remain categorized.
Hosted finished-revision CI is pending under [PLAN](../../../PLAN.md).
This is one document, separate from the frozen corporate 27/27 / 258/258 and
selected 50/50; the two controls add no document count. Existing references,
font programs and renderer options remain unchanged.
