using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Lokad.Svg.Chrome;

/// <summary>Provides serialized request/response access to one DevTools WebSocket.</summary>
internal sealed class DevToolsConnection : IAsyncDisposable
{
    internal const int MaximumMessageBytes = 16 * 1024 * 1024;
    // 8,388,608 RGBA pixels need about 43 MiB after PNG/base64 expansion even
    // without useful compression. Only the screenshot reply gets this allowance.
    internal const int MaximumScreenshotMessageBytes = 64 * 1024 * 1024;
    internal const int MaximumRetainedEvents = 64;
    internal static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(5);
    private readonly WebSocket _socket;
    private readonly Queue<string> _events = new();
    private int _nextCommandId;

    internal DevToolsConnection(WebSocket socket) { _socket = socket; }

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

    /// <remarks>Commands are serialized by the caller. Only Page.captureScreenshot admits a 64 MiB reply; commands, events and other replies retain the 16 MiB bound. Incoming buffers grow within that bound and are released on success, failure or cancellation. The caller owns cancellation and disposes the connection.</remarks>
    internal async Task<string> SendAsync(string method, string parametersJson, CancellationToken cancellationToken)
    {
        int id = checked(++_nextCommandId);
        byte[] command = CreateCommand(id, method, parametersJson);
        await _socket.SendAsync(command, WebSocketMessageType.Text, true, cancellationToken);
        int maximumReplyBytes = method == "Page.captureScreenshot" ? MaximumScreenshotMessageBytes : MaximumMessageBytes;
        while (true)
        {
            string message = await ReceiveTextMessageAsync(_socket, maximumReplyBytes, cancellationToken);
            using JsonDocument response = JsonDocument.Parse(message);
            if (!response.RootElement.TryGetProperty("id", out JsonElement responseId) || !responseId.TryGetInt32(out int responseCommandId) || responseCommandId != id)
            {
                if (Encoding.UTF8.GetByteCount(message) > MaximumMessageBytes) throw new InvalidDataException("Unmatched DevTools message exceeds the configured 16 MiB transport limit.");
                if (response.RootElement.TryGetProperty("method", out JsonElement methodName) && methodName.ValueKind == JsonValueKind.String) RetainEvent(message);
                continue;
            }
            ValidateSuccessfulResponse(method, response.RootElement);
            return message;
        }
    }

    /// <summary>Waits for a DevTools event while preserving events observed during a prior command.</summary>
    internal async Task WaitForEventAsync(string method, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        int queuedCount = _events.Count;
        for (int index = 0; index < queuedCount; index++)
        {
            string queued = _events.Dequeue();
            if (HasMethod(queued, method)) return;
            _events.Enqueue(queued);
        }
        while (true)
        {
            string message = await ReceiveTextMessageAsync(cancellationToken);
            if (HasMethod(message, method)) return;
            using JsonDocument response = JsonDocument.Parse(message);
            if (response.RootElement.TryGetProperty("method", out JsonElement methodName) && methodName.ValueKind == JsonValueKind.String) RetainEvent(message);
        }
    }

    internal static bool HasMethod(string message, string method)
    {
        using JsonDocument response = JsonDocument.Parse(message);
        return response.RootElement.TryGetProperty("method", out JsonElement methodName) && methodName.ValueKind == JsonValueKind.String && methodName.GetString() == method;
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
        => await ReceiveTextMessageAsync(socket, MaximumMessageBytes, cancellationToken);

    /// <remarks>The explicit limit applies to assembled UTF-8 bytes, including fragmented frames. Storage grows only to that limit; cancellation is passed to each receive. The socket remains owned by the caller.</remarks>
    internal static async Task<string> ReceiveTextMessageAsync(WebSocket socket, int maximumMessageBytes, CancellationToken cancellationToken)
    {
        if (maximumMessageBytes is <= 0 or > MaximumScreenshotMessageBytes) throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream message = new();
        while (true)
        {
            WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close) throw new WebSocketException("DevTools closed its WebSocket before replying.");
            if (received.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("DevTools sent a non-text protocol message.");
            long requiredLength = message.Length + received.Count;
            if (requiredLength > maximumMessageBytes) throw new InvalidDataException("DevTools message exceeds the configured " + maximumMessageBytes / (1024 * 1024) + " MiB transport limit.");
            if (requiredLength > message.Capacity) message.Capacity = GetExpandedMessageCapacity(message.Capacity, checked((int)requiredLength), maximumMessageBytes);
            message.Write(buffer, 0, received.Count);
            if (received.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
        }
    }

    private static int GetExpandedMessageCapacity(int currentCapacity, int requiredLength, int maximumMessageBytes)
    {
        int capacity = Math.Min(maximumMessageBytes, Math.Max(currentCapacity, 64 * 1024));
        while (capacity < requiredLength)
        {
            if (capacity > maximumMessageBytes / 2) return maximumMessageBytes;
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
        if (_socket.State == WebSocketState.Open)
        {
            using CancellationTokenSource handshake = new(CloseHandshakeTimeout);
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", handshake.Token); }
            catch (OperationCanceledException) { _socket.Abort(); }
            catch (WebSocketException) { _socket.Abort(); }
        }
        _socket.Dispose();
    }

    private void RetainEvent(string message)
    {
        if (_events.Count >= MaximumRetainedEvents) throw new InvalidDataException("DevTools event retention exceeds its configured limit.");
        _events.Enqueue(message);
    }
}
