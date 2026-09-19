namespace Lokad.Svg.Chrome;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        if (arguments.Length != 1)
        {
            Console.Error.WriteLine("Usage: Lokad.Svg.Chrome <path-to-pinned-chrome>");
            return 2;
        }

        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(30));
        await using ChromeProcess chrome = await ChromeProcess.LaunchAsync(arguments[0], cancellation.Token);
        await using DevToolsConnection connection = await DevToolsConnection.ConnectAsync(chrome.DebuggerAddress, cancellation.Token);
        Console.WriteLine(await connection.SendAsync("Browser.getVersion", "{}", cancellation.Token));
        return 0;
    }
}
