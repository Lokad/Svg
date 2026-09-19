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
superseded after three identical fresh captures. The renderer now uses analytic
intersection area for finite bounded decimal/px rectangle edges. For a rectangle
containing a fractional device edge, its source-over branch uses Chrome-calibrated
retained-alpha and straight-channel rounding. The candidate PNG hash is
`99bf47f4676f21a870a02eac1afbc42eeafbd181ca5e94fbe96dc2b9099adac7`, with
zero differing decoded RGBA pixels against this reference. Fractional paths,
point-list shapes, and non-axis-aligned geometry remain outside this subset.
