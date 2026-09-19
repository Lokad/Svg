using System.Net.WebSockets;
using System.Text;
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

    private readonly record struct Frame(WebSocketMessageType MessageType, bool EndOfMessage, byte[] Data);

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly Queue<Frame> _frames;

        internal FakeWebSocket(params Frame[] frames) => _frames = new Queue<Frame>(frames);

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
            Frame frame = _frames.Dequeue();
            frame.Data.AsSpan().CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(frame.Data.Length, frame.MessageType, frame.EndOfMessage));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
