namespace Lokad.Svg;

using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Xml;

public static partial class SvgConverter
{
    /// <summary>Inspects root sizing using the conversion CSS/unit rules without constructing a scene or accessing fonts.</summary>
    /// <remarks>Uses the logical viewport and parsing limits from options. Output scale, background, font snapshot and raster limits do not affect the result. Unsupported drawing content is not a renderability check.</remarks>
    public static SvgDocumentDimensions InspectDimensions(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        using IMemoryScope? scope = options.MemoryBudget?.CreateScope();
        RenderBudget budget = scope is null ? new() : new(scope);
        try { return SvgDocumentSizing.Inspect(svg, options, budget, cancellationToken); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
    }

    /// <summary>Converts SVG bytes to an independently owned PNG result.</summary>
    public static ReadOnlyMemory<byte> ConvertToPng(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        if (options.MemoryBudget is not null) throw new InvalidOperationException("Governed output requires ConvertToOwnedPng, caller memory or a stream.");
        using RasterizedConversion conversion = PrepareRasterizationCore(svg, options, cancellationToken);
        return PngEncoder.Encode(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts SVG bytes to PNG storage owned by the disposable result.</summary>
    /// <remarks>The optional host budget is borrowed. A governed result owns a sibling scope created before preparation; its charge survives operation cleanup until result disposal. Finish readers before disposing the result.</remarks>
    public static SvgPngResult ConvertToOwnedPng(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        IMemoryScope? owner = options.MemoryBudget?.CreateScope();
        try
        {
            using RasterizedConversion conversion = PrepareRasterizationCore(svg, options, cancellationToken);
            byte[] png = PngEncoder.Encode(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels,
                conversion.Budget, owner, options.Limits.MaximumEncodedPngBytes, cancellationToken);
            return new(png, owner);
        }
        catch { owner?.Dispose(); throw; }
    }

    /// <summary>Schedules one owned-result conversion on the thread pool; its scope stays alive through result disposal.</summary>
    public static Task<SvgPngResult> ConvertToOwnedPngAsync(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
        => Task.Run(() => ConvertToOwnedPng(svg, options, cancellationToken), cancellationToken);

    /// <summary>Schedules one conversion on the thread pool.</summary>
    public static Task<ReadOnlyMemory<byte>> ConvertToPngAsync(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(() => ConvertToPng(svg, options, cancellationToken), cancellationToken);
    }

    /// <summary>Converts SVG bytes into caller-owned destination memory.</summary>
    public static int ConvertToPng(ReadOnlyMemory<byte> svg, Memory<byte> destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        if (svg.Span.Overlaps(destination.Span)) throw new ArgumentException("PNG destination must not overlap SVG input memory.", nameof(destination));
        using RasterizedConversion conversion = PrepareRasterizationCore(svg, options, cancellationToken);
        return PngEncoder.EncodeInto(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts SVG bytes to caller-owned writable stream output.</summary>
    public static int ConvertToPng(ReadOnlyMemory<byte> svg, Stream destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        using RasterizedConversion conversion = PrepareRasterization(svg, options, cancellationToken);
        return PngEncoder.EncodeTo(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Schedules one destination conversion on the thread pool.</summary>
    public static Task<int> ConvertToPngAsync(ReadOnlyMemory<byte> svg, Memory<byte> destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(() => ConvertToPng(svg, destination, options, cancellationToken), cancellationToken);
    }

    /// <summary>Schedules one conversion with asynchronous writes to a caller-owned writable stream.</summary>
    public static Task<int> ConvertToPngAsync(ReadOnlyMemory<byte> svg, Stream destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
            using RasterizedConversion conversion = PrepareRasterization(svg, options, cancellationToken);
            return await PngEncoder.EncodeToAsync(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene to an independently owned PNG result.</summary>
    internal static ReadOnlyMemory<byte> ConvertToPng(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Encodes a retained scene while the caller's sibling scope owns the returned PNG storage and charge.</summary>
    internal static byte[] ConvertToOwnedPng(SvgRoot root, SvgRenderOptions options, RenderBudget budget, IMemoryScope? outputScope, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, outputScope,
            options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene into caller-owned destination memory.</summary>
    internal static int ConvertToPng(SvgRoot root, Memory<byte> destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeInto(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene to a caller-owned writable stream.</summary>
    internal static int ConvertToPng(SvgRoot root, Stream destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeTo(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Schedules one retained prepared-scene conversion on the thread pool.</summary>
    internal static Task<ReadOnlyMemory<byte>> ConvertToPngAsync(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
        => Task.Run(() => ConvertToPng(root, options, budget, cancellationToken), cancellationToken);

    /// <summary>Schedules one retained prepared-scene destination conversion on the thread pool.</summary>
    internal static Task<int> ConvertToPngAsync(SvgRoot root, Memory<byte> destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
        => Task.Run(() => ConvertToPng(root, destination, options, budget, cancellationToken), cancellationToken);

    /// <summary>Schedules one retained prepared-scene conversion with asynchronous writes to a caller-owned writable stream.</summary>
    internal static Task<int> ConvertToPngAsync(SvgRoot root, Stream destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
            ValidatePreparedInvocation(options, budget, cancellationToken);
            using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
            return await PngEncoder.EncodeToAsync(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private static void ValidateInvocation(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (svg.Length > options.Limits.MaximumInputBytes)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        }
    }

    private static void ValidatePreparedInvocation(SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Creates the budgeted parse-and-raster lifetime shared by output APIs and internal pixel validation.</summary>
    /// <remarks>The caller owns the result and finishes all pixel borrowers before disposal. Capture pixels are tightly packed sRGB straight-alpha RGBA8; RGB at zero alpha is unspecified until PNG encoding clears it. No PNG encoding or encoded-size validation occurs here.</remarks>
    internal static RasterizedConversion PrepareRasterization(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        return PrepareRasterizationCore(svg, options, cancellationToken);
    }

    private static RasterizedConversion PrepareRasterizationCore(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        IMemoryScope? scope = options.MemoryBudget?.CreateScope();
        RenderBudget budget = scope is null ? new() : new(scope);
        SvgRoot root = default;
        try
        {
            try
            {
                using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, cancellationToken);
                options = preparation.ResolveRasterOptions(options);
                root = LowerPreparedScene(svg, options, budget, preparation, cancellationToken);
            }
            catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
            return new RasterizedConversion(budget, root, Rasterize(root, options, budget, cancellationToken), scope);
        }
        catch
        {
            root.Dispose();
            scope?.Dispose();
            throw;
        }
    }

    /// <summary>Lowers one prepared document and admits its scene for raw or retained conversion.</summary>
    /// <remarks>Preparation, options/fonts and budget stay borrowed; the caller owns a returned scene.
    /// Unsupported scenes are disposed before throwing their retained diagnostic. Parsing observes
    /// caller cancellation and owns failure cleanup. Each call uses its invocation's existing owners.</remarks>
    internal static SvgRoot LowerPreparedScene(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget,
        SvgDocumentPreparation preparation, CancellationToken token)
    {
        SvgAffineTransform outputMap = preparation.GetOutputMap(options);
        SvgRoot root = SvgRootParser.ParsePrepared(svg, options.Limits, budget, options.FontSnapshot, token,
            outputMap, options.Viewport, options.OutputScale, preparation);
        if (!root.HasUnsupportedChild) return root;
        root.Dispose();
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.", null, root.UnsupportedDiagnostic);
    }

    /// <summary>Owns the parsed scene, surface and invocation scope until their synchronous pixel borrower or output path finishes.</summary>
    /// <remarks>Surface pixels are borrowed read-only until disposal; callers must not retain or mutate them, or race disposal with access. The supplied host budget remains borrowed.</remarks>
    internal sealed class RasterizedConversion : IDisposable
    {
        private readonly SvgRoot _root;
        private readonly IMemoryScope? _scope;

        internal RasterizedConversion(RenderBudget budget, SvgRoot root, RasterizedSurface surface, IMemoryScope? scope)
        {
            Budget = budget;
            _root = root;
            Surface = surface;
            _scope = scope;
        }

        internal RenderBudget Budget { get; }

        internal RasterizedSurface Surface { get; }

        /// <summary>Releases the surface, scene and owned invocation scope after all pixel borrowers finish.</summary>
        /// <remarks>Teardown is synchronous and uncancelable; the host budget remains borrowed.
        /// Only the conversion owner disposes this lifetime, without concurrent pixel access.</remarks>
        public void Dispose()
        {
            Surface.Dispose();
            _root.Dispose();
            _scope?.Dispose();
        }
    }
}
