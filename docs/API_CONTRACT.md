# API contract

Every conversion API requires explicit `SvgRenderOptions` and `CancellationToken`.
The owned-memory result, once rendering exists, will be independently owned and
valid indefinitely. Destination overloads may partially write on cancellation or
failure. Input and destination memory must not overlap.

`SvgRenderOptions` is immutable. A logical viewport is optional and distinct from
the positive output scale. Its `SvgRenderLimits` are validated at construction;
renderer-owned live storage, input bytes, dimensions, output bytes and work are
separate limits. The initial defaults are the provisional limits in PLAN.md.

Memory-to-memory asynchronous conversion will schedule one synchronous conversion
on the thread pool and will observe cancellation throughout the implementation.
It will not create per-shape or per-pixel tasks.
