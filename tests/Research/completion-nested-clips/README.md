# Nested clip-child intersections (A5)

The [manifest](manifest.json) retains four 64×64 transparent CC0 controls:
two/three-level group clip-resource chains, a chain in pattern content and a
transformed/clipped rectangle child with an unaffected sibling. The directory
[LICENSE](LICENSE) preserves original source bytes. Windows/Linux references are exact.

Nested paths/rectangle children retain intersections and source maps. Shared
native stencils preserve color/coverage through groups, tiles, symbols and mask
sources; final owners register content. Resource chains use iterative binding
with cycle detection. Tests retain denial/cancellation/cleanup/retry and source
parity. Clipping children does not admit `clip-path` on the `clipPath` container.
