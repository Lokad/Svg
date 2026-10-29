using System.Collections.Concurrent;
using System.Security.Cryptography;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>
/// Shares immutable native images within one rendering fixture. Concurrent reads
/// are supported; callers borrow pixels read-only. Clear only after readers finish.
/// </summary>
internal sealed class NativeReferenceImages
{
    private readonly ConcurrentDictionary<string, Lazy<Reference>> _images = new(StringComparer.Ordinal);

    internal RgbaImage Get(string path, string expectedSha256)
    {
        Reference reference = _images.GetOrAdd(path, static referencePath =>
            new Lazy<Reference>(() => Read(referencePath))).Value;
        // Validate each row's claim, including rows sharing an already loaded path.
        if (reference.Sha256 != expectedSha256)
            throw new InvalidDataException("Native reference hash differs: " + path);
        return reference.Image;

        static Reference Read(string referencePath)
        {
            byte[] bytes = File.ReadAllBytes(referencePath);
            return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), PngRgbaDecoder.Decode(bytes, 16_384));
        }
    }

    internal void Clear() => _images.Clear();

    private sealed record Reference(string Sha256, RgbaImage Image);
}
