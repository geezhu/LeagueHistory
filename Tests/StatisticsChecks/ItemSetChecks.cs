using System.Net;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class ItemSetChecks
{
    public static async Task RunAsync()
    {
        var (itemSet, alternate, otherMode) = CheckOpggExport();
        var mayhem = await CheckAramMetaExportAsync(otherMode);
        foreach (string scenario in new[] { "success", "rejected", "unverified", "malformed" })
        {
            await CheckSaveAsync(itemSet, alternate, mayhem, scenario);
        }
        Console.WriteLine(
            "PASS: equipment selection, later subtraction, quantities, personal-set preservation and verified saves."
        );
    }

    private static (JObject ItemSet, JObject Alternate, JObject OtherMode) CheckOpggExport()
    {
        var data = JObject.Parse(
            "{\"core_items\":[{\"ids\":[1,2,3],\"pick_rate\":0.2},{\"ids\":[1,2,4],\"pick_rate\":0.7}],\"last_items\":[{\"ids\":[1]},{\"ids\":[3]},{\"ids\":[4]},{\"ids\":[4]},{\"ids\":[5]}]}"
        );
        var build = new OpggBuild(
            new("global", "ranked", "emerald_plus", "mid", 103),
            "16.17",
            data,
            new JObject(),
            Array.Empty<OpggRunes>()
        );
        var options = OpggItemSetService.Options(data, "core_items");
        if (OpggItemSetService.Ids(options[0])[2] != 4)
        {
            throw new Exception("Equipment default must use highest usage");
        }

        if (
            !OpggItemSetService.Later(data, options[0]).SequenceEqual(new[] { 3, 5 })
            || !OpggItemSetService.Later(data, options[1]).SequenceEqual(new[] { 4, 5 })
        )
        {
            throw new Exception(
                "Later items must subtract only the selected core and retain source order without duplicates"
            );
        }

        var itemSet = OpggItemSetService.Create(
            build,
            "Ahri",
            options[0],
            JObject.Parse("{\"ids\":[1056,2003,2003]}"),
            JObject.Parse("{\"ids\":[3020]}")
        );
        if ((int?)itemSet["blocks"]?[0]?["items"]?[1]?["count"] != 2)
        {
            throw new Exception("Starter potion quantity must survive export");
        }

        var alternate = OpggItemSetService.Create(build, "Ahri", options[1], null, null);
        if ((string?)alternate["uid"] != (string?)itemSet["uid"])
        {
            throw new Exception("Changing equipment should update the same managed set");
        }

        var otherMode = OpggItemSetService.Create(
            build with
            {
                Query = build.Query with { Mode = "aram" },
            },
            "Ahri",
            options[0],
            null,
            null
        );
        if ((string?)otherMode["uid"] == (string?)itemSet["uid"] || (int?)otherMode["associatedMaps"]?[0] != 12)
        {
            throw new Exception("Equipment mode and map must be isolated");
        }

        return (itemSet, alternate, otherMode);
    }

    private static async Task<JObject> CheckAramMetaExportAsync(JObject otherMode)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta");
        var page = new AramMetaPage(
            AramMetaIndex.Parse(JObject.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "index.json")))),
            JObject.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "103.json"))),
            true,
            DateTime.UtcNow
        );
        var mayhemBuild = OpggItemSetService.FromAramMeta(page, 103);
        var mayhemCores = OpggItemSetService.Options(mayhemBuild.Data, "core_items");
        var mayhem = OpggItemSetService.Create(
            mayhemBuild,
            "Ahri",
            mayhemCores[0],
            null,
            OpggItemSetService.Options(mayhemBuild.Data, "boots").First(),
            "arammeta"
        );
        if (
            (int?)mayhem["associatedMaps"]?[0] != 12
            || !((string)mayhem["title"]!).StartsWith("[arammeta] ")
            || (string?)mayhem["uid"] == (string?)otherMode["uid"]
        )
        {
            throw new Exception("arammeta sets retain source and map without replacing OP.GG sets");
        }

        if (
            OpggItemSetService
                .Later(mayhemBuild.Data, mayhemCores[0])
                .Intersect(OpggItemSetService.Ids(mayhemCores[0]))
                .Any()
            || ((JArray)mayhem["blocks"]!).Count != 3
        )
        {
            throw new Exception("arammeta exports core, boots and later items without fabricated starter equipment");
        }

        return mayhem;
    }

    private static async Task CheckSaveAsync(JObject itemSet, JObject alternate, JObject mayhem, string scenario)
    {
        using var fixture = new SaveFixture(scenario);
        bool success = false;
        try
        {
            await OpggItemSetService.SaveAsync(fixture.Client, itemSet, CancellationToken.None);
            success = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpRequestException)
        {
            // Expected failures must leave success false; the assertions below verify each scenario.
        }
        if (success != (scenario == "success"))
        {
            throw new Exception("Item-set result must reflect verified save");
        }

        if (scenario == "malformed" && fixture.Writes != 0)
        {
            throw new Exception("Malformed data must not be overwritten");
        }

        if (success)
        {
            await OpggItemSetService.SaveAsync(fixture.Client, alternate, CancellationToken.None);
            if (((JArray)fixture.Document["itemSets"]!).Count != 2)
            {
                throw new Exception("Reapplying must not accumulate item sets");
            }

            await OpggItemSetService.SaveAsync(fixture.Client, mayhem, CancellationToken.None);
            await OpggItemSetService.SaveAsync(fixture.Client, mayhem, CancellationToken.None);
            if (
                ((JArray)fixture.Document["itemSets"]!).Count != 3
                || !JToken.DeepEquals(fixture.Document["itemSets"]?[1], alternate)
            )
            {
                throw new Exception("arammeta reapplication preserves both personal and OP.GG sets without duplicates");
            }
        }
    }

    private sealed class SaveFixture : IDisposable
    {
        private readonly JObject _personal;
        private readonly string _scenario;
        public HttpClient Client { get; }
        public JObject Document { get; private set; }
        public int Writes { get; private set; }

        public SaveFixture(string scenario)
        {
            _scenario = scenario;
            _personal = JObject.Parse("{\"uid\":\"personal\",\"title\":\"My set\",\"custom\":123,\"blocks\":[]}");
            Document = new JObject
            {
                ["accountId"] = 123,
                ["timestamp"] = 5,
                ["itemSets"] = new JArray(_personal.DeepClone()),
            };
            Client = new HttpClient(new Handler(SendAsync)) { BaseAddress = new Uri("https://test.invalid") };
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("current-summoner"))
            {
                return Json(new JObject { ["summonerId"] = 7 });
            }

            if (request.RequestUri.AbsolutePath != "/lol-item-sets/v1/item-sets/7/sets")
            {
                throw new Exception("Wrong item-set account path");
            }

            if (request.Method == HttpMethod.Get)
            {
                return Json(_scenario == "malformed" ? new JObject() : Document);
            }

            if (request.Method != HttpMethod.Put)
            {
                throw new Exception("Unexpected item-set write");
            }

            Writes++;
            var payload = JObject.Parse(await request.Content!.ReadAsStringAsync());
            if (!JToken.DeepEquals(payload["itemSets"]?[0], _personal) || (int?)payload["accountId"] != 123)
            {
                throw new Exception("Personal sets and account metadata must be preserved");
            }

            if (_scenario == "rejected")
            {
                return new(HttpStatusCode.Forbidden);
            }

            if (_scenario == "success")
            {
                Document = payload;
            }

            return Json(new JObject());
        }

        public void Dispose() => Client.Dispose();
    }

    private static HttpResponseMessage Json(JObject value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request);
    }
}
