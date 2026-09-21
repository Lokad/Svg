using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Lokad.Svg.Chrome;

namespace Lokad.Svg.VisualTests;

public sealed class DevToolsConnectionTests
{
    [Fact]
    public async Task Reassembles_fragmented_text_messages()
    {
        FakeWebSocket socket = new(
            new Frame(WebSocketMessageType.Text, false, "{\"id\":"u8.ToArray()),
            new Frame(WebSocketMessageType.Text, false, "1,\"result\":"u8.ToArray()),
            new Frame(WebSocketMessageType.Text, true, "{}}"u8.ToArray()));

        string message = await DevToolsConnection.ReceiveTextMessageAsync(socket, CancellationToken.None);

        Assert.Equal("{\"id\":1,\"result\":{}}", message);
    }

    [Fact]
    public async Task Rejects_closed_websocket_messages()
    {
        FakeWebSocket socket = new(new Frame(WebSocketMessageType.Close, true, []));

        await Assert.ThrowsAsync<WebSocketException>(async () => await DevToolsConnection.ReceiveTextMessageAsync(socket, CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_non_text_websocket_messages()
    {
        FakeWebSocket socket = new(new Frame(WebSocketMessageType.Binary, true, [1]));

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await DevToolsConnection.ReceiveTextMessageAsync(socket, CancellationToken.None));

        Assert.Contains("non-text", exception.Message);
    }

    [Fact]
    public async Task Rejects_fragmented_text_messages_that_exceed_the_transport_limit()
    {
        RepeatingTextWebSocket socket = new(DevToolsConnection.MaximumMessageBytes + 1);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await DevToolsConnection.ReceiveTextMessageAsync(socket, CancellationToken.None));

        Assert.Contains("16 MiB", exception.Message);
    }

    [Fact]
    public void Rejects_protocol_error_responses()
    {
        using JsonDocument response = JsonDocument.Parse("""{"error":{"message":"bad request"}}""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => DevToolsConnection.ValidateSuccessfulResponse("Page.enable", response.RootElement));

        Assert.Equal("DevTools command 'Page.enable' failed: bad request", exception.Message);
    }

    [Fact]
    public void Rejects_evaluation_exception_details()
    {
        using JsonDocument response = JsonDocument.Parse("""{"result":{"exceptionDetails":{"text":"ReferenceError"}}}""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => DevToolsConnection.ValidateSuccessfulResponse("Runtime.evaluate", response.RootElement));

        Assert.Equal("DevTools command 'Runtime.evaluate' evaluation failed: ReferenceError", exception.Message);
    }

    [Fact]
    public void Rejects_invalid_result_responses()
    {
        using JsonDocument response = JsonDocument.Parse("""{"result":null}""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => DevToolsConnection.ValidateSuccessfulResponse("Page.enable", response.RootElement));

        Assert.Equal("DevTools command 'Page.enable' returned an invalid result.", exception.Message);
    }

    [Fact]
    public void Identifies_only_the_requested_DevTools_event()
    {
        Assert.True(DevToolsConnection.HasMethod("""{"method":"Page.loadEventFired","params":{}}""", "Page.loadEventFired"));
        Assert.False(DevToolsConnection.HasMethod("""{"method":"Page.frameStartedLoading","params":{}}""", "Page.loadEventFired"));
        Assert.False(DevToolsConnection.HasMethod("""{"id":1,"result":{}}""", "Page.loadEventFired"));
    }

    [Fact]
    public void Rejects_outgoing_protocol_messages_before_allocating_an_oversized_frame()
    {
        string parameters = new('x', DevToolsConnection.MaximumMessageBytes);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => DevToolsConnection.CreateCommand(1, "Runtime.evaluate", parameters));

        Assert.Contains("16 MiB", exception.Message);
    }

    [Fact]
    public async Task Rejects_oversized_capture_input_before_launching_Chrome()
    {
        byte[] oversized = new byte[ChromeSvgCapture.MaximumSvgBytes + 1];

        await Assert.ThrowsAsync<InvalidDataException>(async () => await ChromeSvgCapture.CaptureAsync("missing", oversized, 1, 1, CancellationToken.None));
    }

    [Fact]
    public async Task Stops_reading_standard_input_at_the_capture_limit()
    {
        using MemoryStream input = new(new byte[ChromeSvgCapture.MaximumSvgBytes + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await ChromeSvgCapture.ReadSvgAsync(input, CancellationToken.None));
    }

    [Fact]
    public async Task Event_flood_fails_fast()
    {
        List<Frame> frames = new();
        for (int index = 0; index <= DevToolsConnection.MaximumRetainedEvents; index++)
            frames.Add(new Frame(WebSocketMessageType.Text, true, "{\"method\":\"Flood.event\",\"params\":{}}"u8.ToArray()));
        frames.Add(new Frame(WebSocketMessageType.Text, true, "{\"id\":1,\"result\":{}}"u8.ToArray()));
        FakeWebSocket socket = new(frames.ToArray());
        DevToolsConnection connection = new(socket);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await connection.SendAsync("Test.flood", "{}", CancellationToken.None));

        Assert.Contains("event retention", exception.Message);
    }

    [Fact]
    public async Task Silent_peer_does_not_hang_disposal()
    {
        FakeWebSocket socket = new() { HangOnClose = true };
        DevToolsConnection connection = new(socket);

        Task dispose = Task.Run(async () => { await connection.DisposeAsync(); });
        Task winner = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(60)));

        Assert.Same(dispose, winner);
        await dispose;
        Assert.True(socket.Aborted);
    }

    [Fact]
    public async Task Cancelled_send_throws_without_completing()
    {
        FakeWebSocket socket = new(new Frame(WebSocketMessageType.Text, true, "{}"u8.ToArray()));
        DevToolsConnection connection = new(socket);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await connection.SendAsync("Test.cmd", "{}", cancellation.Token));
    }
    private readonly record struct Frame(WebSocketMessageType MessageType, bool EndOfMessage, byte[] Data);

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly Queue<Frame> _frames;

        internal FakeWebSocket(params Frame[] frames) => _frames = new Queue<Frame>(frames);

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        internal bool HangOnClose { get; set; }

        internal bool Aborted { get; private set; }

        public override void Abort() => Aborted = true;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => HangOnClose ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose() { }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Frame frame = _frames.Dequeue();
            frame.Data.AsSpan().CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(frame.Data.Length, frame.MessageType, frame.EndOfMessage));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RepeatingTextWebSocket : WebSocket
    {
        private int _remainingBytes;

        internal RepeatingTextWebSocket(int remainingBytes) => _remainingBytes = remainingBytes;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort() { }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose() { }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Count, _remainingBytes);
            buffer.AsSpan(0, count).Clear();
            _remainingBytes -= count;
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, _remainingBytes == 0));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
