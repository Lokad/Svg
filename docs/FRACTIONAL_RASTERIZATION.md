# Fractional rectangle investigation

The `original-fractional-overlap` probe is a compact, original CC0 test for the
next rasterization stage. It has two fractional-coordinate rectangles with 0.5
fill opacity. Three independent fresh processes using the pinned Chrome for
Testing 153.0.8010.52 build produced the same reference hash:

```text
8e2faa670552a1578dc7bc775711f535a8262d1d9eb20432842a31576a7d810b
```

Capture recipe revision 2 explicitly sets the HTML document background to
transparent. The prior recipe left uncovered pixels opaque white, despite the
fixture's transparent-background declaration; its reference was therefore
superseded after three identical fresh captures. The current candidate deliberately fails with `UnsupportedFeature` because its
rectangle rasterizer accepts only integer-valued geometry. It must remain an
explicit failure until a coverage algorithm is selected and validated against
this reference; rounding fractional coordinates to integers is not acceptable.

The next experiment should isolate one fractional edge at a time, then compare
coverage values, alpha quantization, and source-over rounding with Chrome at
multiple scales. Any proposed analytic or sampled implementation must first make
these reduced probes exact before this fixture changes from `unsupported`.
