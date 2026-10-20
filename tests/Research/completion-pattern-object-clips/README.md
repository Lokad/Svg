# Object-box pattern content clips (A2)

The [manifest](manifest.json) retains four 64×64 transparent CC0 controls: a
clipped pattern leaf, vector group, image group and a transformed multi-child
group with a zero-width stroked target/unaffected sibling. The directory
[LICENSE](LICENSE) preserves original bytes. Windows/Linux references are exact.

Pattern groups collect complete pre-clip object bounds before ordinary native
clip binding. Bounds lookup and registration use the tile scene owner, including
recorded image bounds. User-space groups keep their own route. Resource checks
cover denial, cancellation, cleanup and prepared retry for image/degenerate cases.
