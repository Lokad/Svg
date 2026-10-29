using System.Net;

internal sealed class HttpFontSmokeHandler(byte[] manifest, byte[] font) : HttpMessageHandler
{
    internal int Requests { get; private set; }
    /// <summary>Borrows the request until completion and returns caller-owned controlled content without network access.</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal) == true ? manifest : font)
        });
    }
}
