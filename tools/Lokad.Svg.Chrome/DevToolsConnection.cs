using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Lokad.Svg.Chrome;

/// <summary>Provides serialized request/response access to one DevTools WebSocket.</summary>
internal sealed class DevToolsConnection : IAsyncDisposable
{
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private readonly ClientWebSocket _socket;
    private int _nextCommandId;

    private DevToolsConnection(ClientWebSocket socket) { _socket = socket; }

    internal static async Task<DevToolsConnection> ConnectAsync(string debuggerAddress, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("http://" + debuggerAddress + "/json/version", cancellationToken));
        string endpoint = document.RootElement.GetProperty("webSocketDebuggerUrl").GetString() ?? throw new InvalidDataException("DevTools endpoint is missing.");
        ClientWebSocket socket = new();
        try
        {
            await socket.ConnectAsync(new Uri(endpoint), cancellationToken);
            return new DevToolsConnection(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static async Task<DevToolsConnection> ConnectToTargetAsync(string debuggerAddress, string targetId, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        while (true)
        {
            using JsonDocument targets = JsonDocument.Parse(await client.GetStringAsync("http://" + debuggerAddress + "/json/list", cancellationToken));
            foreach (JsonElement target in targets.RootElement.EnumerateArray())
            {
                if (target.GetProperty("id").GetString() != targetId) continue;
                string endpoint = target.GetProperty("webSocketDebuggerUrl").GetString() ?? throw new InvalidDataException("Target endpoint is missing.");
                ClientWebSocket socket = new();
                try
                {
                    await socket.ConnectAsync(new Uri(endpoint), cancellationToken);
                    return new DevToolsConnection(socket);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            await Task.Delay(25, cancellationToken);
        }
    }

    internal async Task<string> SendAsync(string method, string parametersJson, CancellationToken cancellationToken)
    {
        int id = checked(++_nextCommandId);
        byte[] command = Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + parametersJson + "}");
        await _socket.SendAsync(command, WebSocketMessageType.Text, true, cancellationToken);
        while (true)
        {
            string message = await ReceiveTextMessageAsync(cancellationToken);
            using JsonDocument response = JsonDocument.Parse(message);
            if (!response.RootElement.TryGetProperty("id", out JsonElement responseId) || responseId.GetInt32() != id) continue;
            if (response.RootElement.TryGetProperty("error", out JsonElement error)) throw new InvalidDataException("DevTools command '" + method + "' failed: " + GetErrorMessage(error));
            if (!response.RootElement.TryGetProperty("result", out _)) throw new InvalidDataException("DevTools command '" + method + "' returned neither a result nor an error.");
            return message;
        }
    }

    private async Task<string> ReceiveTextMessageAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream message = new();
        while (true)
        {
            WebSocketReceiveResult received = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close) throw new WebSocketException("DevTools closed its WebSocket before replying.");
            if (received.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("DevTools sent a non-text protocol message.");
            long requiredLength = message.Length + received.Count;
            if (requiredLength > MaximumMessageBytes) throw new InvalidDataException("DevTools message exceeds the configured 16 MiB transport limit.");
            if (requiredLength > message.Capacity) message.Capacity = checked((int)requiredLength);
            message.Write(buffer, 0, received.Count);
            if (received.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
        }
    }

    private static string GetErrorMessage(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String) return message.GetString() ?? "an unspecified protocol error";
        return error.GetRawText();
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open) await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        _socket.Dispose();
    }
}
