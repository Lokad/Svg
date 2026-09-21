using Lokad.Svg;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.FontProbe;

internal static class Program
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const long MaximumProgramBytes = 64L * 1024 * 1024;

    private static async Task<int> Main(string[] arguments)
    {
        if (!TryParseArguments(arguments, out ProbeOptions? options) || options is null)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.FontProbe --manifest <manifest.json> --pack-id <pack-id> --font-root <directory> --svg <input.svg> --output <output.png>");
            return 2;
        }

        try
        {
            byte[] manifest = await ReadFileAsync(options.ManifestPath, MaximumManifestBytes, CancellationToken.None).ConfigureAwait(false);
            byte[] svg = await ReadFileAsync(options.SvgPath, SvgRenderLimits.Default.MaximumInputBytes, CancellationToken.None).ConfigureAwait(false);
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifest, options.PackId);
            var provider = new LocalFontProgramSourceProvider(options.FontRoot);

            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None).ConfigureAwait(false);
            await using var destination = new FileStream(options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int pngBytes = await prepared.ConvertToPngAsync(destination, CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine("outcome=success");
            Console.WriteLine("pngBytes=" + pngBytes);
            Console.WriteLine("requestedFontPrograms=" + provider.RequestedProgramCount);
            return 0;
        }
        catch (LocalFileException)
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
    }

    private static bool TryParseArguments(string[] arguments, out ProbeOptions? options)
    {
        options = null;
        if (arguments.Length != 10) return false;

        string? manifestPath = null;
        string? packId = null;
        string? fontRoot = null;
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

        if (manifestPath is null || packId is null || fontRoot is null || svgPath is null || outputPath is null) return false;
        options = new ProbeOptions(manifestPath, packId, fontRoot, svgPath, outputPath);
        return true;
    }

    private static async ValueTask<byte[]> ReadFileAsync(string path, long maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = stream.Length;
            if (length < 0 || length > maximumBytes || length > int.MaxValue) throw new LocalFileException();

            var bytes = new byte[checked((int)length)];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new LocalFileException();
                offset += read;
            }

            var extra = new byte[1];
            if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0) throw new LocalFileException();
            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LocalFileException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new LocalFileException();
        }
        catch (UnauthorizedAccessException)
        {
            throw new LocalFileException();
        }
        catch (ArgumentException)
        {
            throw new LocalFileException();
        }
        catch (NotSupportedException)
        {
            throw new LocalFileException();
        }
    }

    private sealed record ProbeOptions(string ManifestPath, string PackId, string FontRoot, string SvgPath, string OutputPath);

    private sealed class LocalFontProgramSourceProvider : ISvgFontProgramSourceProvider
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
                throw new LocalFileException();
            }
            catch (ArgumentException)
            {
                throw new LocalFileException();
            }
            catch (NotSupportedException)
            {
                throw new LocalFileException();
            }

            _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        internal int RequestedProgramCount => Volatile.Read(ref _requestedProgramCount);

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(file);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestedProgramCount);
            try
            {
                string relativePath = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(_fontRoot, relativePath));
                if (!candidate.StartsWith(_fontRootPrefix, _pathComparison)) throw new LocalFileException();
                long maximumBytes = Math.Min(file.ByteSize, MaximumProgramBytes);
                return ValueTask.FromResult<ISvgFontProgramSource?>(new LocalFontProgramSource(candidate, maximumBytes));
            }
            catch (LocalFileException)
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
                return await ReadFileAsync(path, maximumBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (LocalFileException)
            {
                throw new SvgException(SvgErrorCode.UnresolvedResource, "The local font program could not be read.");
            }
        }
    }

    private sealed class LocalFileException : Exception;
}
