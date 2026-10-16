# API contract

SVG XML admits at most one declaration-only DOCTYPE before the root, including
bare, SYSTEM and PUBLIC declarations. External identifiers are inert: the renderer
does not load a DTD, expand declared entities or apply DTD defaults. Internal
subsets, including empty `[]`, duplicate declarations and malformed XML produce
`MalformedInput`. Predefined XML entities retain their ordinary XML behavior.
Root parsing, font discovery and output-scale probing share this policy without
rewriting input bytes or changing XML encoding interpretation. Prolog validation
reserves an additional fixed 32 KiB workspace, releases it before ordinary parsing,
and charges bounded traversal work with cancellation checkpoints. Input, node,
depth and per-element attribute limits continue to apply.

Drawable `path` data follows SVG's complete-prefix error handling: completed
segments before the first malformed command remain renderable, and an error
before a complete moveto paints nothing. The full attribute still consumes the
configured command/work limits and observes cancellation. This policy does not
recover malformed XML, missing required path data, clip/mask path syntax or
unsupported geometry/paint combinations; those retain categorized failures.

Pattern vector content borrows a bounded tile scene owned by its parent scene.
The admitted direct leaves share ordinary geometry and recorded paint, then
render into a premultiplied tile surface under the same memory, pixel, dimension,
work and cancellation limits. Child raster work consumes the parent's remaining
work allowance; failure/cancellation releases scratch while preserving the
borrowed scene for retry. Repeat sampling retains its existing bounded filtering.
Local pattern templates accept `href` and `xlink:href` (`href` wins), inherit
omitted attributes and content, and retain explicit local overrides. Backward and
forward chains resolve iteratively; aliases borrow a shared scene with independent
grids. Fractional vector tile dimensions retain their original period after
integral texture rasterization. Missing/cyclic local or external template targets
remain `UnsupportedFeature`; duplicates and negative dimensions are `MalformedInput`.
Recursive pattern paints and currently unsupported tile containers/coordinate
systems produce categorized errors; the admitted subset is in the support matrix.

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
separate limits. Default values and the approximate memory policy are in PLAN.md.
The internal ledger uses conservative estimates and aggregate allowances for
material renderer-owned allocations, exposes current/peak/denial diagnostics, and
releases reservations deterministically. Directionally, no more than 20% of
conversion-attributable live memory should be unaccounted for across representative
workloads; this is an engineering target, not an exact guarantee at every instant.
Shared runtime/host overhead and borrowed storage remain separate. Byte-exact
small-object accounting and GC-round retirement proofs are not completion contracts;
size/work limits, cancellation, cleanup and output integrity remain required.

For admitted SVG content, an explicit `SvgViewport` supplies the
outer logical dimensions and overrides root `width`/`height`; a root `viewBox`, when
present, still maps its user coordinates into that viewport.
The per-axis dimension cap applies after output scaling. Exceeding it with valid
integral dimensions returns `SvgErrorCode.LimitExceeded` before surface allocation
or output writes. Nonpositive or non-finite resolved output dimensions remain `UnsupportedFeature`; fractional dimensions round to the nearest device pixel (midpoint away from zero) before that check, matching an explicit rounded viewport byte-for-byte; malformed SVG values retain their parsing errors.

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
standalone-TrueType core metrics and any present `name`/`OS/2` metadata table
structure, and retains at most 256 face records and 32 MiB
of borrowed bytes for its disposable lifetime. Snapshot access after disposal
throws `ObjectDisposedException`; callers must not dispose a snapshot while a read
or conversion using it is in progress. Disposal invalidates snapshot and
`SvgPreparedFont` member access, but cannot revoke a `ReadOnlyMemory<byte>` view
already obtained from `SvgPreparedFont.Bytes`; that view remains caller-owned and
subject to the same validity and immutability obligation. Prepared direct-text
layout and outline decoding reuse those validated core metrics without reparsing
them for every cmap, advance, or glyph-structure query. These queries neither parse
SVG nor perform storage I/O. The request-based snapshot overload resolves all explicit family/style requests,
preflights distinct declared byte sizes and same-hash consistency, then selectively
asks a provider for each distinct program. Its
SVG-driven overload accepts an SVG-namespaced root and discovers live supplied-face
requirements through the admitted stylesheet, inline and inherited font context,
nested text and local instances. Live font-relative marker lengths also participate;
unused, hidden and empty marker users do not acquire a face. Discovery and metric/
program validation obey the caller's input, XML depth, attribute, node, memory and
work limits before rendering. Preparation owns its discovery storage and releases
it on every exit; BCL XML workspace uses the separate conservative allowance.

The finite [G9 boundary](../PLAN.md#g9-supplied-font-labels-and-text-paths) and
[SUPPORT_MATRIX](SUPPORT_MATRIX.md) define admitted supplied-font labels and text
paths. Text/CDATA and nested `tspan` order, inherited/per-span font selection,
positioning lists, spacing/anchors/baselines, textLength, affine glyph maps,
ordinary paints/strokes, decorations and local text paths use shared bounded
layout and source replay. Catalog fallback is explicit; missing required programs
or glyphs cannot silently select a machine font. Complex-script shaping, wrapping,
vertical text and the other enumerated formats/features remain outside scope.

`xml:space="default"` collapses spaces, tabs, carriage returns and line feeds to
one inter-word space and trims leading/trailing runs; `xml:space="preserve"`
converts tabs and line breaks to spaces while retaining leading, trailing and
repeated spaces. Inherited CSS `white-space: normal`/`pre` takes precedence over
`xml:space` when declared. Hidden and fill-less characters retain their advances;
display-none content is excluded. Effective paint follows normalized character
intervals through glyph layout and text-path replay. Positioning, span offsets,
chunk anchoring and list consumption retain their qualified source-order rules.

A validated version-zero horizontal format-zero legacy `kern` subtable or the
admitted unfiltered GPOS PairPos format-1 first-glyph X-advance lookup adjusts
adjacent glyphs; GPOS takes precedence when both are present. The bounded default
ligature subset is specified below. Native unrotated strike placement and affine/
text-path mapping retain their platform distinctions.

Embedded `name` data is structurally validated but does not override manifest family
selection. `OS/2` version-specific table bounds remain checked; its style fields
do not select a face. Validated hhea/OS2 alphabetic line metrics are retained for
text-path fragment paint placement. Native unrotated paint rectangles truncate
to 1/64 pixel before tangent rotation and device strike placement; semantic curve
measurement retains its float coordinates.
Mapped Linux glyph masks are constructed in bounded glyph-local bitmaps using
the shared geometry rasterizer. The fractional strike origin precedes local edge
construction; the document's integer origin and SVG viewport clip apply when
painting the finished mask. Bitmap dimensions, pixels, storage and traversal
consume the conversion's existing limits, budget and work; cancellation and
failure release scratch without disposing the borrowed supplied face.
Its direct text content is limited to 32,768 UTF-16 characters across one
conversion before layout; subtree reader nodes and cmap/advance lookup traversal
consume the conversion's parser-work limit. Positioned glyph storage is reserved from the conversion's geometry budget
before allocation and released after each direct text run is lowered.

Default standard ligatures for contiguous Basic Latin letters use the supplied
font's Latin (or DFLT) default-language `liga` feature. The bounded subset is one
unfiltered type-4 lookup with format-1 subtables, coverage formats 1/2 and two
to eight components, in font preference order. Nonzero letter spacing and
positioned-span boundaries prevent merging; UTF-16 source positions remain
attached to the resulting clusters. Text-path kerning enters the glyph advance
before device rounding and midpoint/tangent placement. Other selected lookup
types, flags, multiple lookups and general shaping remain unsupported.

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
