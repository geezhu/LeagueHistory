using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Clients;

public sealed class LiveMatchClient : IDisposable
{
    private readonly HttpClient _http;

    public LiveMatchClient(HttpMessageHandler? handler = null)
    {
        // Exception is limited to this loopback-only game client; never reused for remote statistics.
        _http = new HttpClient(
            handler
                ?? new HttpClientHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                        request.RequestUri?.Host == "127.0.0.1" && request.RequestUri.Port == 2999,
                }
        )
        {
            BaseAddress = new Uri("https://127.0.0.1:2999/"),
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

    public async Task<LiveMatchSnapshot> GetAsync(CancellationToken token) =>
        LiveMatchSnapshot.Parse(JObject.Parse(await _http.GetStringAsync("liveclientdata/allgamedata", token)));

    public void Dispose() => _http.Dispose();
}
