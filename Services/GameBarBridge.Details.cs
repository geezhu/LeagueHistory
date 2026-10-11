using System.Threading.Channels;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed partial class GameBarBridge
{
    private sealed record DetailScope(string Session, int[] Heroes);

    private volatile DetailScope _detailScope = new("", []);
    private readonly Channel<JObject> _detailPending = Channel.CreateBounded<JObject>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
    );

    public async Task<OverlayDetailRequest?> ReadDetailRequestAsync()
    {
        var scope = _detailScope;
        if (_disposed || scope.Session.Length == 0)
        {
            return null;
        }

        string directory = _testDirectory ?? _directory;
        if (directory.Length == 0)
        {
            return null;
        }

        try
        {
            string path = Path.Combine(directory, "detail-request.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096)
            {
                return null;
            }

            var value = JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            if (scope != _detailScope)
            {
                return null;
            }

            return OverlayDetailRequest.Parse(
                value,
                scope.Session,
                scope.Heroes,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or Newtonsoft.Json.JsonException
                        or FormatException
                        or OverflowException
                        or ArgumentException
            )
        {
            return null;
        }
    }

    public void PublishDetail(OverlayDetailRequest request, JObject content)
    {
        var scope = _detailScope;
        if (
            _disposed
            || request.Session != scope.Session
            || request.Slot < 0
            || request.Slot >= scope.Heroes.Length
            || scope.Heroes[request.Slot] != request.ChampionId
        )
        {
            return;
        }

        var data = (JObject)content.DeepClone();
        data["protocol"] = 1;
        data["requestId"] = request.Id;
        data["session"] = request.Session;
        data["championId"] = request.ChampionId;
        data["revision"] = Guid.NewGuid().ToString("N");
        data["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _detailPending.Writer.TryWrite(data);
    }

    private async Task WriteDetailLoop()
    {
        await foreach (var data in _detailPending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            string directory = _testDirectory ?? _directory;
            if (directory.Length == 0 || (string?)data["session"] != _detailScope.Session)
            {
                continue;
            }

            string temporary = Path.Combine(directory, "detail-" + _instance + ".tmp");
            try
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(
                        temporary,
                        data.ToString(Newtonsoft.Json.Formatting.None),
                        new System.Text.UTF8Encoding(false)
                    )
                    .ConfigureAwait(false);
                File.Move(temporary, Path.Combine(directory, "detail.json"), true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (IOException) { }
            }
        }
    }
}
