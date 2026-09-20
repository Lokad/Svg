# API contract

Every conversion API requires explicit `SvgRenderOptions` and `CancellationToken`.
The owned-memory result is independently owned and valid indefinitely. The
corresponding synchronous and asynchronous overloads also write into caller-owned
`Memory<byte>` or a writable `Stream`, returning the exact PNG byte count on
success. Destination overloads may partially write on cancellation or failure.
Input and destination memory must not overlap. Both memory-destination APIs reject
overlap visible through their spans with `ArgumentException` (`destination`)
before parsing or writing; disjoint slices of shared storage remain valid. Both
may be non-array-backed `Memory` instances; conversion does not require an
`ArraySegment`. Callers remain responsible for aliases the memory provider cannot
expose, including distinct virtual mappings of the same storage or stream-backed
aliases. Input memory must stay valid and unchanged until conversion completes.

`SvgRenderOptions` is immutable. A logical viewport is optional and distinct from
the positive output scale. Its `SvgRenderLimits` are validated at construction;
renderer-owned live storage, input bytes, dimensions, output bytes and work are
separate limits. The initial defaults are the provisional limits in PLAN.md. The
internal ledger reserves live renderer-owned storage before the corresponding
allocation, exposes current/peak/denial diagnostics to the conversion context, and
releases reservations deterministically.

For the current supported rectangle subset, an explicit `SvgViewport` supplies the
outer logical dimensions and overrides root `width`/`height`; a root `viewBox`, when
present, still maps its user coordinates into that viewport.
The per-axis dimension cap applies after output scaling. Exceeding it with valid
integral dimensions returns `SvgErrorCode.LimitExceeded` before surface allocation
or output writes. Nonpositive, non-finite or fractional resolved output dimensions
remain `UnsupportedFeature`; malformed SVG values retain their parsing errors.

Memory-to-memory asynchronous conversion schedules one synchronous conversion on
the thread pool and observes cancellation throughout the implementation. It does
not create per-shape or per-pixel tasks. Asynchronous stream conversion likewise
does parsing/raster work once on the thread pool, then awaits each destination
write in order; it does not use synchronous `Stream.Write` or leave a background
producer after its task completes.

Stream destinations must be writable but need not be seekable. They remain open,
are not flushed by conversion, and receive PNG data in cancellation-checked chunks
no larger than 64 KiB (apart from smaller PNG headers). The stream forms do not
allocate an owned encoded PNG result; their compressed workspace remains bounded
by the configured encoded-PNG and renderer-memory limits.

If the supplied token is already canceled, the asynchronous entry points return a
canceled task; they do not throw cancellation synchronously before scheduling.
