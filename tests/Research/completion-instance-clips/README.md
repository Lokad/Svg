# Retained instance content clips (A3)

The [manifest](manifest.json) retains ten 64×64 transparent CC0 controls for leaf
and nested-group clips in symbols, local definition groups and markers, including
an object-box marker group. Forward resources, affine placement, separate
instances, unaffected siblings and clip coordinates on a retained `use` have
independent controls. The directory [LICENSE](LICENSE) preserves original source
bytes. Frozen Windows/Linux references are exact; the unclipped marker remains
a separate discriminator.

Clipped leaves force source replay; retained containers bind shared native/
object-box clips. Implicit symbol viewport clipping applies without a viewBox.
Resource checks cover material denial, cancellation, cleanup and prepared retry.
