# Filtered gradient source fidelity (A6)

The [manifest](manifest.json) retains five CC0 controls: four original 64×64
transparent gradient/blur, pattern gradient/blur, solid filtered-tile and
unfiltered-gradient cases, plus a 96×64 discriminator for translated zero,
asymmetric and wider blurs. The directory [LICENSE](LICENSE) preserves original
source bytes. Frozen Windows/Linux references are exact.

Gradient capture reuses native padded source-bitmap bounds from image paints.
Dithering retains the logical source phase when compact storage omits the halo;
shader coordinates and color arithmetic stay separate from storage cropping.
