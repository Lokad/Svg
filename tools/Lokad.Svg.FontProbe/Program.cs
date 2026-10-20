using Lokad.Svg;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.FontProbe;

internal static class Program
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const long MaximumProgramBytes = 64L * 1024 * 1024;
    private const long MaximumCachedProgramBytes = 32L * 1024 * 1024;
    private const int MaximumDownloadAttempts = 3;

    private static async Task<int> Main(string[] arguments)
    {
        if (!TryParseArguments(arguments, out ProbeOptions? options) || options is null)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.FontProbe (--manifest <manifest.json> --font-root <directory> | --source-uri <https://source-root/>) --pack-id <pack-id> --svg <input.svg|-> --output <output.png>");
            return 2;
        }

        try
        {
            byte[] svg = await ReadSvgInputAsync(options.SvgPath, SvgRenderLimits.Default.MaximumInputBytes, CancellationToken.None).ConfigureAwait(false);
            byte[] manifest;
            IProbeFontProgramSourceProvider provider;
            using HttpClient? httpClient = options.SourceUri is null ? null : new HttpClient();
            if (options.ManifestPath is not null && options.FontRoot is not null)
            {
                manifest = await ReadLocalFileAsync(options.ManifestPath, MaximumManifestBytes, CancellationToken.None).ConfigureAwait(false);
                provider = new LocalFontProgramSourceProvider(options.FontRoot);
            }
            else
            {
                Uri sourceRoot = NormalizeSourceUri(options.SourceUri ?? throw new HostSourceException());
                ValidatePackId(options.PackId);
                Uri packRoot = new(sourceRoot, options.PackId + "/");
                manifest = await DownloadHttpAsync(httpClient ?? throw new HostSourceException(), new Uri(packRoot, "manifest.json"), MaximumManifestBytes, MaximumDownloadAttempts, CancellationToken.None).ConfigureAwait(false);
                provider = new HttpFontProgramSourceProvider(httpClient, packRoot, MaximumProgramBytes, MaximumCachedProgramBytes, MaximumDownloadAttempts);
            }
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, options.PackId);

            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None).ConfigureAwait(false);
            await using var destination = new FileStream(options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int pngBytes = await prepared.ConvertToPngAsync(destination, CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine("outcome=success");
            Console.WriteLine("pngBytes=" + pngBytes);
            Console.WriteLine("requestedFontPrograms=" + provider.RequestedProgramCount);
            return 0;
        }
        catch (HostSourceException)
        {
            Console.WriteLine("outcome=host-error");
            return 4;
        }
        catch (SvgException exception)
        {
            Console.WriteLine("outcome=svg-error");
            Console.WriteLine("svgErrorCode=" + exception.ErrorCode);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("outcome=cancelled");
            return 3;
        }
        catch (Exception)
        {
            Console.WriteLine("outcome=host-error");
            return 4;
        }

        static void ValidatePackId(string packId)
        {
            if (string.IsNullOrWhiteSpace(packId) || packId is "." or ".." || packId.IndexOfAny(['/', '\\', ':', '?', '#']) >= 0) throw new HostSourceException();
            string decoded;
            try { decoded = Uri.UnescapeDataString(packId); }
            catch (UriFormatException) { throw new HostSourceException(); }
            if (decoded is "." or ".." || decoded.IndexOfAny(['/', '\\']) >= 0) throw new HostSourceException();
        }
    }

    private static bool TryParseArguments(string[] arguments, out ProbeOptions? options)
    {
        options = null;
        if (arguments.Length is not (8 or 10)) return false;

        string? manifestPath = null;
        string? packId = null;
        string? fontRoot = null;
        string? sourceUri = null;
        string? svgPath = null;
        string? outputPath = null;
        for (int index = 0; index < arguments.Length; index += 2)
        {
            string option = arguments[index];
            string value = arguments[index + 1];
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (option)
            {
                case "--manifest" when manifestPath is null:
                    manifestPath = value;
                    break;
                case "--pack-id" when packId is null:
                    packId = value;
                    break;
                case "--font-root" when fontRoot is null:
                    fontRoot = value;
                    break;
                case "--source-uri" when sourceUri is null:
                    sourceUri = value;
                    break;
                case "--svg" when svgPath is null:
                    svgPath = value;
                    break;
                case "--output" when outputPath is null:
                    outputPath = value;
                    break;
                default:
                    return false;
            }
        }

        bool localSource = manifestPath is not null && fontRoot is not null && sourceUri is null;
        bool httpSource = manifestPath is null && fontRoot is null && sourceUri is not null;
        if ((localSource || httpSource) && packId is not null && svgPath is not null && outputPath is not null)
        {
            options = new ProbeOptions(packId, svgPath, outputPath, manifestPath, fontRoot, sourceUri);
        }
        return true;
    }

    private static async ValueTask<byte[]> ReadLocalFileAsync(string path, long maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await ReadBoundedStreamAsync(stream, maximumBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HostSourceException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new HostSourceException();
        }
        catch (UnauthorizedAccessException)
        {
            throw new HostSourceException();
        }
        catch (ArgumentException)
        {
            throw new HostSourceException();
        }
        catch (NotSupportedException)
        {
            throw new HostSourceException();
        }
    }

    private static ValueTask<byte[]> ReadSvgInputAsync(string path, long maximumBytes, CancellationToken cancellationToken)
        => path == "-" ? ReadBoundedStreamAsync(Console.OpenStandardInput(), maximumBytes, cancellationToken) : ReadLocalFileAsync(path, maximumBytes, cancellationToken);

    /// <summary>Copies one caller-selected SVG byte stream without seeking, decoding or retaining bytes beyond its configured limit.</summary>
    internal static async ValueTask<byte[]> ReadBoundedStreamAsync(Stream source, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("SVG input must be readable.", nameof(source));
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var destination = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) return destination.ToArray();
            if (read > maximumBytes - destination.Length) throw new HostSourceException();
            destination.Write(chunk, 0, read);
        }
    }

    private static Uri NormalizeSourceUri(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new HostSourceException();
        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static async ValueTask<byte[]> DownloadHttpAsync(HttpClient client, Uri uri, long maximumBytes, int maximumAttempts, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadHttpSingleAttemptAsync(client, uri, maximumBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HostSourceException)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
            {
                if (attempt == maximumAttempts) throw new HostSourceException();
            }
        }
    }

    private static async ValueTask<byte[]> DownloadHttpSingleAttemptAsync(HttpClient client, Uri uri, long maximumBytes, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is long declared && declared > maximumBytes) throw new HostSourceException();
        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            int read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) return destination.ToArray();
            if (read > maximumBytes - destination.Length) throw new HostSourceException();
            destination.Write(chunk, 0, read);
        }
    }

    private sealed record ProbeOptions(string PackId, string SvgPath, string OutputPath, string? ManifestPath, string? FontRoot, string? SourceUri);

    private interface IProbeFontProgramSourceProvider : ISvgFontProgramSourceProvider
    {
        int RequestedProgramCount { get; }
    }

    private sealed class LocalFontProgramSourceProvider : IProbeFontProgramSourceProvider
    {
        private readonly string _fontRoot;
        private readonly string _fontRootPrefix;
        private readonly StringComparison _pathComparison;
        private int _requestedProgramCount;

        internal LocalFontProgramSourceProvider(string fontRoot)
        {
            try
            {
                _fontRoot = Path.GetFullPath(fontRoot);
                _fontRootPrefix = Path.EndsInDirectorySeparator(_fontRoot) ? _fontRoot : _fontRoot + Path.DirectorySeparatorChar;
            }
            catch (IOException)
            {
                throw new HostSourceException();
            }
            catch (ArgumentException)
            {
                throw new HostSourceException();
            }
            catch (NotSupportedException)
            {
                throw new HostSourceException();
            }

            _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        int IProbeFontProgramSourceProvider.RequestedProgramCount => Volatile.Read(ref _requestedProgramCount);

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(file);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestedProgramCount);
            try
            {
                string relativePath = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(_fontRoot, relativePath));
                if (!candidate.StartsWith(_fontRootPrefix, _pathComparison)) throw new HostSourceException();
                long maximumBytes = Math.Min(file.ByteSize, MaximumProgramBytes);
                return ValueTask.FromResult<ISvgFontProgramSource?>(new LocalFontProgramSource(candidate, maximumBytes));
            }
            catch (HostSourceException)
            {
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local font program could not be acquired.");
            }
            catch (IOException)
            {
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local font program could not be acquired.");
            }
            catch (ArgumentException)
            {
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local font program could not be acquired.");
            }
            catch (NotSupportedException)
            {
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local font program could not be acquired.");
            }
        }
    }

    private sealed class LocalFontProgramSource(string path, long maximumBytes) : ISvgFontProgramSource
    {
        public string StableId => "local-font-program";

        public async ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await ReadLocalFileAsync(path, maximumBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (HostSourceException)
            {
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local font program could not be read.");
            }
        }
    }

    internal sealed class HttpFontProgramSourceProvider : IProbeFontProgramSourceProvider
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _packRoot;
        private readonly long _maximumProgramBytes;
        private readonly long _maximumCachedProgramBytes;
        private readonly int _maximumAttempts;
        private readonly object _sync = new();
        private readonly Dictionary<string, HttpFontProgramSource> _sources = new(StringComparer.Ordinal);
        private long _cachedProgramBytes;
        private int _requestedProgramCount;

        internal HttpFontProgramSourceProvider(HttpClient httpClient, Uri packRoot, long maximumProgramBytes, long maximumCachedProgramBytes, int maximumAttempts)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(packRoot);
            if (maximumProgramBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumProgramBytes));
            if (maximumCachedProgramBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCachedProgramBytes));
            if (maximumAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
            _httpClient = httpClient;
            _packRoot = packRoot;
            _maximumProgramBytes = maximumProgramBytes;
            _maximumCachedProgramBytes = maximumCachedProgramBytes;
            _maximumAttempts = maximumAttempts;
        }

        public int RequestedProgramCount => Volatile.Read(ref _requestedProgramCount);

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(file);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestedProgramCount);
            lock (_sync)
            {
                if (!_sources.TryGetValue(file.RelativePath, out HttpFontProgramSource? source))
                {
                    source = new HttpFontProgramSource(_httpClient, new Uri(_packRoot, file.RelativePath), Math.Min(file.ByteSize, _maximumProgramBytes), _maximumAttempts, this);
                    _sources.Add(file.RelativePath, source);
                }
                return ValueTask.FromResult<ISvgFontProgramSource?>(source);
            }
        }

        internal bool TryReserveCache(int byteCount)
        {
            while (true)
            {
                long current = Volatile.Read(ref _cachedProgramBytes);
                if (byteCount > _maximumCachedProgramBytes - current) return false;
                if (Interlocked.CompareExchange(ref _cachedProgramBytes, current + byteCount, current) == current) return true;
            }
        }
    }

    private sealed class HttpFontProgramSource(HttpClient httpClient, Uri uri, long maximumBytes, int maximumAttempts, HttpFontProgramSourceProvider provider) : ISvgFontProgramSource
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private byte[]? _cachedBytes;

        public string StableId => "http-font-program";

        public async ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _cachedBytes) is byte[] cached) return cached;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cachedBytes is byte[] rechecked) return rechecked;
                byte[] bytes;
                try { bytes = await DownloadHttpAsync(httpClient, uri, maximumBytes, maximumAttempts, cancellationToken).ConfigureAwait(false); }
                catch (HostSourceException) { throw new SvgException(SvgErrorCode.UnresolvedResource, "The HTTP font program could not be read."); }
                if (provider.TryReserveCache(bytes.Length)) Volatile.Write(ref _cachedBytes, bytes);
                return bytes;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    internal sealed class HostSourceException : Exception;
}
