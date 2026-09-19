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
