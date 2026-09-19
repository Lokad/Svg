# Fractional rectangle investigation

The `original-fractional-overlap` probe is a compact, original CC0 test for the
next rasterization stage. It has two fractional-coordinate rectangles with 0.5
fill opacity. Three independent fresh processes using the pinned Chrome for
Testing 153.0.8010.52 build produced the same reference hash:

```text
609018c06f337221d8e0d5e943844a668a9c9cbf41df66398b4eaa32869f1b3e
```

The current candidate deliberately fails with `UnsupportedFeature` because its
rectangle rasterizer accepts only integer-valued geometry. It must remain an
explicit failure until a coverage algorithm is selected and validated against
this reference; rounding fractional coordinates to integers is not acceptable.

The next experiment should isolate one fractional edge at a time, then compare
coverage values, alpha quantization, and source-over rounding with Chrome at
multiple scales. Any proposed analytic or sampled implementation must first make
these reduced probes exact before this fixture changes from `unsupported`.
