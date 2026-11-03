# Clip children and independent references

A group inside `clipPath` contributes no clip silhouette. Its IDs still identify
ordinary drawing sources: selecting a leaf omits its ancestors' transforms, while
selecting the group retains its own transform. A group selected by a clip-resource
`use` contributes no contour. The existing dormant-group replay handles these roles;
no clip-group flattening or separate geometry pipeline is added.

These original CC0 controls isolate the distinctions:

| Input | Behavior protected |
| --- | --- |
| [Direct](direct.svg) | Independent baseline for an ordinary clip child. |
| [Group only](group-only.svg) | An ignored group leaves an empty clip. |
| [Mixed](mixed.svg) | Valid sibling geometry survives; group geometry is excluded. |
| [Unused](unused.svg) | Dormant resource content does not invalidate unrelated drawing. |
| [Selected leaf](selected-leaf.svg) | A descendant ID remains usable without its enclosing transform. |
| [Selected group](selected-group.svg) | A group ID remains usable with its authored transform. |
| [Clip-selected leaf](clip-selected-leaf.svg) | A separate clip can consume the descendant geometry. |
| [Clip-selected group](clip-selected-group.svg) | Container references do not become clip contours. |

The [capture recipe](capture.json) preserves three healthy fresh processes per
platform. Seven [canonical rendering cases](../../Lokad.Svg.VisualTests/SvgClipChildRoleReferenceTests.cs)
keep distinct reference roles; the direct input remains the diagnostic baseline.
Equal pixels do not make these roles interchangeable. Direct
[mechanisms](../../Lokad.Svg.Tests/SvgClipChildRoleTests.cs) check admission and
empty clip bindings, font/scalar selection and duplicate IDs. One
[parser lifecycle contract](../../Lokad.Svg.Tests/SvgClipGroupOwnershipTests.cs)
reuses the existing borrowed-storage/refusal/cancellation/retry helper; one
[prepared contract](../../Lokad.Svg.Tests/OpenTypeFontTests.ClipGroups.cs) acquires
only the selected font and leaves sibling text inactive.

The [masking specification](https://www.w3.org/TR/css-masking-1/#elementdef-clippath)
distinguishes clip silhouettes from container references; the pinned native captures
resolve the ignored-group behavior. [Qualification](../svgweb/clip-child-roles.json)
records the shared implementation, source hashes, timings and original-file outcome.
