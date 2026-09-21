# API contract

Every `SvgConverter` conversion API requires explicit `SvgRenderOptions` and `CancellationToken`.
The owned-memory result is independently owned and valid indefinitely. The
corresponding synchronous and asynchronous overloads also write into caller-owned
`Memory<byte>` or a writable `Stream`, returning the exact PNG byte count on
success. Destination overloads may partially write on cancellation or failure.
`SvgConverter` input and destination memory must not overlap. Both memory-destination APIs reject
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
not probe storage or request unrelated pack files. A caller can supply a verified
snapshot through render options to exercise the deliberately narrow direct-text
pipeline; catalog acquisition is not implicitly invoked by rendering.
`ISvgFontProgramSource` is the explicit caller-owned byte boundary. Binding a
catalog resolution wraps that source with declared byte-count/SHA-256 validation;
it neither copies nor caches the returned program. Source storage must therefore
remain valid and unchanged for its consumer lifetime.
When a selected provider has no source, or its provider/source boundary returns a
categorized failure, the library preserves its error code and emits a generic
message with a bounded `SvgFontDiagnostic` at the `Source` stage. The diagnostic
contains requested/resolved family, face identity and (when future text parsing
retains it) element ID; it never contains font bytes, source text or caller
transport details.
`SvgFontSnapshot.CreateAsync` is the explicit preparation building block for
already-selected programs. It re-verifies caller-owned bytes, validates the
standalone-TrueType core metrics, and retains at most 256 face records and 32 MiB
of borrowed bytes for its disposable lifetime. Snapshot access after disposal
throws `ObjectDisposedException`; callers must not dispose a snapshot while a read
or conversion using it is in progress. Disposal invalidates snapshot and
`SvgPreparedFont` member access, but cannot revoke a `ReadOnlyMemory<byte>` view
already obtained from `SvgPreparedFont.Bytes`; that view remains caller-owned and
subject to the same validity and immutability obligation. Prepared direct-text
layout and outline decoding reuse those validated core metrics without reparsing
them for every cmap, advance, or glyph-structure query. It neither parses SVG nor performs
storage I/O. Its request-based overload resolves all explicit family/style requests,
preflights distinct declared byte sizes and same-hash consistency, then selectively
asks a provider for each distinct program. Its
SVG-driven overload accepts only an SVG-namespaced root and discovers inherited
single-family direct-`text` presentation attributes, admitted root-stylesheet
properties, and inline-style properties (`font-family`, `font-weight`,
`font-style`, `font-size`) within either default limits or caller-supplied input,
XML depth, attribute, node and work limits through metric validation and program
integrity verification before doing the same; CSS family lists and text-run
retention remain unsupported. It validates root viewport plus inherited container
and direct-text presentation/opacity syntax before provider access. Once it sees
rendered content outside the admitted path, it suppresses later text-font
acquisition. A snapshot can already be supplied through render options. The
renderer's experimental direct-text path accepts only direct
single-face text/CDATA content with default XML whitespace normalization (space, tab, carriage return and line feed collapse to one inter-word space and leading/trailing runs are trimmed); it does not make SVG text a supported feature and
accepts nested attribute-free `tspan` elements only as text concatenation; span styling, positioning lists and general shaping remain unsupported. Its presentation,
admitted root-stylesheet and inline `style` font subset is `font-family`,
`font-weight`, `font-style` and `font-size`; admitted scalar positioning is `x`,
`y`, `dx`, `dy` and `text-anchor` (`start`, `middle`, `end`), with a bounded `id`
retained only for font diagnostics.
Its direct text content is limited to 32,768 UTF-16 characters across one
conversion before layout; subtree reader nodes and cmap/advance lookup traversal
consume the conversion's parser-work limit. Positioned glyph storage is reserved from the conversion's geometry budget
before allocation and released after each direct text run is lowered.

`SvgPreparedDocument.CreateAsync` is the single-conversion preparation owner. It
discovers selected supplied fonts and lowers the admitted scene under the supplied
render limits, retaining its verified snapshot. It carries accumulated preparation
work into rasterization and encoding. It exposes the same six `ConvertToPng` and
`ConvertToPngAsync` output forms as `SvgConverter`, but exactly one may be called;
conversion completion, failure, cancellation, or `Dispose` releases its snapshot
lease. The caller keeps SVG bytes valid and unchanged until `CreateAsync` completes
and font bytes valid and unchanged for the prepared document's lifetime.

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
