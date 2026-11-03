# Native scalar math profiles

These companions preserve the original Linux references while accounting for a
different native `atan2f`. The same assemblies pass the original references with
glibc 2.39; switching only the isolated libc/libm/loader to Ubuntu's glibc 2.43
reproduces all ten Ubuntu 26 CI differences.

[Capture evidence](capture.json) records independent native results before test
selection changed:

- The [native scalar probe](atan2-profile.c) distinguishes the implementations at
  the unrelated input `(1, 7)`. Tests inspect this BCL operation, never renderer
  output. An unrecognized probe result fails qualification.
- The [original pinned Skia function](endpoint-probe.cpp), with a small recording
  adapter, reproduces all 36 legacy rows. Its 36 newer rows exactly equal the
  existing Windows table, so the data bytes are shared while provenance stays distinct.
- Three stable browser observations per profile cover all 26 existing geometry
  inputs. Only five boxes need [numeric companions](text-path-boxes.json); the
  original files and values remain unchanged. The curved character's native angle
  also changes by one scalar ULP.
- Four existing pattern cases retain their own [image companions](image-capture.json),
  each from three healthy, identical captures with the original source, font
  programs and browser executable. The renderer matches all four exactly.

The [shared selector](../../Shared/NativeMathProfile.cs) only chooses independently
measured references. The original rendering cases still run individually; one
additional integrity case protects the new captures. No renderer change, fixture/API
product, test exclusion or tolerance is introduced. Regeneration procedures belong
in [tools](../../../tools/README.md#native-math-reference-profiles).

The SVG-derived companions retain the repository's [MIT license](../../../LICENSE.txt).
The Skia function retains its BSD attribution and [notice](../../../THIRD_PARTY_NOTICES.txt).
