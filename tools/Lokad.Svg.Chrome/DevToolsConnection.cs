using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Lokad.Svg.Chrome;

/// <summary>Provides serialized request/response access to one DevTools WebSocket.</summary>
internal sealed class DevToolsConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private int _nextCommandId;

    private DevToolsConnection(ClientWebSocket socket) { _socket = socket; }

    internal static async Task<DevToolsConnection> ConnectAsync(string debuggerAddress, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("http://" + debuggerAddress + "/json/version", cancellationToken));
        string endpoint = document.RootElement.GetProperty("webSocketDebuggerUrl").GetString() ?? throw new InvalidDataException("DevTools endpoint is missing.");
        ClientWebSocket socket = new();
        await socket.ConnectAsync(new Uri(endpoint), cancellationToken);
        return new DevToolsConnection(socket);
    }

    internal async Task<string> SendAsync(string method, string parametersJson, CancellationToken cancellationToken)
    {
        int id = checked(++_nextCommandId);
        byte[] command = Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + parametersJson + "}");
        await _socket.SendAsync(command, WebSocketMessageType.Text, true, cancellationToken);
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            WebSocketReceiveResult received = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType != WebSocketMessageType.Text || !received.EndOfMessage) throw new InvalidDataException("DevTools message framing is unsupported.");
            string message = Encoding.UTF8.GetString(buffer, 0, received.Count);
            using JsonDocument response = JsonDocument.Parse(message);
            if (response.RootElement.TryGetProperty("id", out JsonElement responseId) && responseId.GetInt32() == id) return message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open) await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        _socket.Dispose();
    }
}
