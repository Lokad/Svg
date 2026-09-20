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

`SvgRenderOptions` is immutable. Its five-argument constructor may carry a
caller-owned prepared `SvgFontSnapshot`; the caller must keep that snapshot and
its font bytes valid and undisposed until conversion completes. A logical viewport
is optional and distinct from the positive output scale. Its `SvgRenderLimits` are validated at construction;
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

`Lokad.Svg.Fonts.SvgFontPackCatalog` parses the public OoxPdf 0.1.4 manifest
format from caller-owned bytes without network or filesystem access. It validates
the requested pack ID, safe relative paths, file byte counts and SHA-256 metadata,
then resolves requested/resolved family aliases, styles and configured fallback
families deterministically. `AcquireAsync` asks an explicit caller-owned
`ISvgFontProgramSourceProvider` for only the selected file and verifies that
program before returning; a missing source is `UnresolvedResource`. The provider
owns I/O, retries, caching, concurrency and its source lifetime. The library does
not probe storage, retain acquired bytes or request unrelated pack files, and the
current renderer does not yet consume catalog entries to render SVG text.
`ISvgFontProgramSource` is the explicit caller-owned byte boundary. Binding a
catalog resolution wraps that source with declared byte-count/SHA-256 validation;
it neither copies nor caches the returned program. Source storage must therefore
remain valid and unchanged for its consumer lifetime.
When a selected provider has no source, the `UnresolvedResource` exception carries
a bounded `SvgFontDiagnostic`: failure stage, requested/resolved family, face
identity and (when future text parsing retains it) element ID. It never contains
font bytes or source text.
`SvgFontSnapshot.CreateAsync` is the explicit preparation building block for
already-selected programs. It re-verifies caller-owned bytes, validates the
standalone-TrueType core metrics, and retains at most 256 face records and 32 MiB
of borrowed bytes for its disposable lifetime. It neither parses SVG nor performs
storage I/O. Its request-based overload resolves explicit family/style requests
through a catalog and selectively asks a provider for each distinct program. Its
SVG-driven overload discovers inherited single-family `text`/`tspan` attributes
and inline-style properties (`font-family`, `font-weight`, `font-style`) before
doing the same; CSS family lists, stylesheet font properties and text-run
retention remain unsupported. A snapshot can already be supplied through render
options, but text parsing and glyph painting remain unsupported at this stage.

Memory-to-memory asynchronous conversion schedules one synchronous conversion on
the thread pool and observes cancellation throughout the implementation. It does
not create per-shape or per-pixel tasks. Asynchronous stream conversion likewise
does parsing/raster work once on the thread pool, then awaits each destination
write in order; it does not use synchronous `Stream.Write` or leave a background
producer after its task completes.

Opaque background initialization checks cancellation in batches of at most 4,096
pixels after the surface allocation. This bounds additional background writes after
the token is observed; it is not a wall-clock latency guarantee, since allocation
and runtime scheduling remain outside the renderer's control.

Stream destinations must be writable but need not be seekable. They remain open,
are not flushed by conversion, and receive PNG data in cancellation-checked chunks
no larger than 64 KiB (apart from smaller PNG headers). The stream forms do not
allocate an owned encoded PNG result; their compressed workspace remains bounded
by the configured encoded-PNG and renderer-memory limits.

If the supplied token is already canceled, the asynchronous entry points return a
canceled task; they do not throw cancellation synchronously before scheduling.
