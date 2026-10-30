# Leaf clip backend retirement

Leaf polygon clips keep their source geometry and bind native clip coverage.
The former destructive intersection backend is removed; native registration,
ordered unions, object-box binding, nested clips and independent mask snapshots
retain their existing owners and numerical paths.

The [producer](../../../src/Lokad.Svg/Parsing/SvgDefinitionParser.cs) and
[resource replay](../../../src/Lokad.Svg/Parsing/SvgResourceUseReplay.cs) establish
these conditions before [leaf resolution](../../../src/Lokad.Svg/Parsing/SvgClipPathSyntax.cs):

| Producer | Retained dispatch |
| --- | --- |
| Straight path or rectangle | A single contour already uses native registration. |
| Curves, compound paths and resolved child unions | Explicit native replay; containment retains a single original rectangle. |
| Painted union fallback | A single original contour or an already native union, with its original child owners. |
| Empty or disabled clip | Empty boxes, object-box handling or disabled-resource suppression. |
| Referenced clip content | Exactly one contour; empty/group sources publish an empty box. Deferred references resolve before leaf clipping. |

On source `defafc23e`, a temporary guard throws if positive geometry reaches the
old intersection branch. All 35,723 core and 46,132 visual checks pass on Windows
and Linux under .NET 11 with that guard. Both test projects use the same guarded
assembly bytes. This runtime evidence complements the producer audit; test counts
or matching images alone do not establish unreachability.

The deletion removes 335 lines: four replacement-paint paths, contour intersection,
convex-ring traversal and their area/edge helpers. Native registration statements
and code outside leaf resolution are unchanged. All existing test sources, case
identities, reference images, platform differences and tolerances are retained.

Final affected qualification passes 8,585 core and 2,783 native/capture checks per
Windows/Linux .NET 11/.NET 10 combination, with warning-free builds. Every selected
identity matches the guarded run; every previous ownership-selection case remains.
