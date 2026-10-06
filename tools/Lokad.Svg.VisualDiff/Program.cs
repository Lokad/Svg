namespace Lokad.Svg.VisualDiff;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (arguments.Length == 4 && arguments[0] == "--cohort")
        {
            try
            {
                CorporateCohortMeasurement.Run(arguments[1], arguments[2], arguments[3], CancellationToken.None);
                return 0;
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }
        }
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.VisualDiff <reference.png> <candidate.png>");
            Console.Error.WriteLine("       Lokad.Svg.VisualDiff --cohort <repository> <captures-directory> <report-directory>");
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
