# Windows font span compatibility

The [six-row SVG](windows-font-span-extension.svg) uses two original CC0 static
TrueType programs with identical outlines and metrics, and distinct internal
families. The fixture named Courier New contains no commercial font bytes.
Its source family exercises an independently measured Windows span extension;
catalog aliases do not determine that policy.

The [declaration](declaration.json) fixes both programs, three sizes, fractional
origins and intended selection. [Independent cmap/hmtx metrics](metrics.json)
cover all rows. The [manifest](manifest.json) records Chrome 153.0.8010.52
Windows/Linux references frozen before candidate comparison: three healthy
stable captures per platform, image/inline agreement, intended custom-font
reports and changed-program controls for both fonts.

Offline raw/prepared tests verify acquisition, selection, asset identities,
independent metrics and exact pixels. Focused controls distinguish the exact
embedded family, case variants, absent names and a host alias to another program.
This document qualifies the shared metadata/coverage behavior; it does not
establish compatibility with any particular Courier New version or private pack.
All fixture assets are [CC0](LICENSE.txt).
