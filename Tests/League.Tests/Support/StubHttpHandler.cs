using System.Net;

namespace League.Tests.Support;

/// <summary>Answers every request with one file's content and records the requests; no request leaves the process.</summary>
internal sealed class StubHttpHandler(string path, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Requests.Add(request.RequestUri!);
        var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, token));
        return new HttpResponseMessage(status) { Content = content };
    }
}
