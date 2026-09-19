using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Lokad.Svg.Chrome;

/// <summary>Provides serialized request/response access to one DevTools WebSocket.</summary>
internal sealed class DevToolsConnection : IAsyncDisposable
{
    internal const int MaximumMessageBytes = 16 * 1024 * 1024;
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
        byte[] command = CreateCommand(id, method, parametersJson);
        await _socket.SendAsync(command, WebSocketMessageType.Text, true, cancellationToken);
        while (true)
        {
            string message = await ReceiveTextMessageAsync(cancellationToken);
            using JsonDocument response = JsonDocument.Parse(message);
            if (!response.RootElement.TryGetProperty("id", out JsonElement responseId) || !responseId.TryGetInt32(out int responseCommandId) || responseCommandId != id) continue;
            ValidateSuccessfulResponse(method, response.RootElement);
            return message;
        }
    }

    /// <summary>Rejects a matched DevTools response that cannot represent a successful command.</summary>
    internal static void ValidateSuccessfulResponse(string method, JsonElement response)
    {
        if (response.TryGetProperty("error", out JsonElement error)) throw new InvalidDataException("DevTools command '" + method + "' failed: " + GetErrorMessage(error));
        if (!response.TryGetProperty("result", out JsonElement result)) throw new InvalidDataException("DevTools command '" + method + "' returned neither a result nor an error.");
        if (result.ValueKind != JsonValueKind.Object) throw new InvalidDataException("DevTools command '" + method + "' returned an invalid result.");
        if (result.TryGetProperty("exceptionDetails", out JsonElement exceptionDetails)) throw new InvalidDataException("DevTools command '" + method + "' evaluation failed: " + GetErrorMessage(exceptionDetails));
    }

    internal static byte[] CreateCommand(int id, string method, string parametersJson)
    {
        string prefix = "{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":";
        int byteCount;
        try
        {
            byteCount = checked(Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(parametersJson) + 1);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException("DevTools command exceeds the configured 16 MiB transport limit.");
        }
        if (byteCount > MaximumMessageBytes) throw new InvalidDataException("DevTools command exceeds the configured 16 MiB transport limit.");
        byte[] command = new byte[byteCount];
        int written = Encoding.UTF8.GetBytes(prefix, command);
        written += Encoding.UTF8.GetBytes(parametersJson, command.AsSpan(written));
        command[written] = (byte)'}';
        return command;
    }

    private async Task<string> ReceiveTextMessageAsync(CancellationToken cancellationToken)
        => await ReceiveTextMessageAsync(_socket, cancellationToken);

    internal static async Task<string> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream message = new();
        while (true)
        {
            WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close) throw new WebSocketException("DevTools closed its WebSocket before replying.");
            if (received.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("DevTools sent a non-text protocol message.");
            long requiredLength = message.Length + received.Count;
            if (requiredLength > MaximumMessageBytes) throw new InvalidDataException("DevTools message exceeds the configured 16 MiB transport limit.");
            if (requiredLength > message.Capacity) message.Capacity = GetExpandedMessageCapacity(message.Capacity, checked((int)requiredLength));
            message.Write(buffer, 0, received.Count);
            if (received.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
        }
    }

    private static int GetExpandedMessageCapacity(int currentCapacity, int requiredLength)
    {
        int capacity = Math.Max(currentCapacity, 64 * 1024);
        while (capacity < requiredLength)
        {
            if (capacity > MaximumMessageBytes / 2) return MaximumMessageBytes;
            capacity *= 2;
        }
        return capacity;
    }

    private static string GetErrorMessage(JsonElement error)
    {
        string message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? "an unspecified protocol error"
            : error.ValueKind == JsonValueKind.Object && error.TryGetProperty("text", out property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? "an unspecified evaluation error"
                : error.GetRawText();
        return message.Length <= 512 ? message : message[..512];
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open) await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        _socket.Dispose();
    }
}
