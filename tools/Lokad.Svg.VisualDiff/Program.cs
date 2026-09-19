namespace Lokad.Svg.VisualDiff;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.VisualDiff <reference.png> <candidate.png>");
            return 2;
        }

        try
        {
            RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(arguments[0]), 16_777_216);
            RgbaImage candidate = PngRgbaDecoder.Decode(File.ReadAllBytes(arguments[1]), 16_777_216);
            PixelComparison comparison = PixelComparer.Compare(reference, candidate);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(comparison));
            return comparison.IsExact ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }
}
