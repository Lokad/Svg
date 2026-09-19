namespace Lokad.Svg.MemoryProbe;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.MemoryProbe <input.svg> <output.png>");
            return 2;
        }

        try
        {
            byte[] png = SvgConverter.ConvertToPng(File.ReadAllBytes(arguments[0]), SvgRenderOptions.Default, CancellationToken.None).ToArray();
            File.WriteAllBytes(arguments[1], png);
            Console.WriteLine(png.Length);
            return 0;
        }
        catch (SvgException exception)
        {
            Console.Error.WriteLine(exception.ErrorCode + ": " + exception.Message);
            return 1;
        }
    }
}
