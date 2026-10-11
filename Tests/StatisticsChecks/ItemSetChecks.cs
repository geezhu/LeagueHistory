using System.Net;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class ItemSetChecks
{
    public static async Task RunAsync()
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
            throw new Exception("Equipment default must use highest usage");
        if (
            !OpggItemSetService.Later(data, options[0]).SequenceEqual(new[] { 3, 5 })
            || !OpggItemSetService.Later(data, options[1]).SequenceEqual(new[] { 4, 5 })
        )
            throw new Exception(
                "Later items must subtract only the selected core and retain source order without duplicates"
            );
        var itemSet = OpggItemSetService.Create(
            build,
            "Ahri",
            options[0],
            JObject.Parse("{\"ids\":[1056,2003,2003]}"),
            JObject.Parse("{\"ids\":[3020]}")
        );
        if ((int?)itemSet["blocks"]?[0]?["items"]?[1]?["count"] != 2)
            throw new Exception("Starter potion quantity must survive export");
        var alternate = OpggItemSetService.Create(build, "Ahri", options[1], null, null);
        if ((string?)alternate["uid"] != (string?)itemSet["uid"])
            throw new Exception("Changing equipment should update the same managed set");
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
            throw new Exception("Equipment mode and map must be isolated");
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta");
        var page = new AramMetaPage(
            AramMetaIndex.Parse(JObject.Parse(File.ReadAllText(Path.Combine(fixture, "index.json")))),
            JObject.Parse(File.ReadAllText(Path.Combine(fixture, "103.json"))),
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
            throw new Exception("arammeta sets retain source and map without replacing OP.GG sets");
        if (
            OpggItemSetService
                .Later(mayhemBuild.Data, mayhemCores[0])
                .Intersect(OpggItemSetService.Ids(mayhemCores[0]))
                .Any()
            || ((JArray)mayhem["blocks"]!).Count != 3
        )
            throw new Exception("arammeta exports core, boots and later items without fabricated starter equipment");
        foreach (string scenario in new[] { "success", "rejected", "unverified", "malformed" })
        {
            var personal = JObject.Parse("{\"uid\":\"personal\",\"title\":\"My set\",\"custom\":123,\"blocks\":[]}");
            var document = new JObject
            {
                ["accountId"] = 123,
                ["timestamp"] = 5,
                ["itemSets"] = new JArray(personal.DeepClone()),
            };
            int writes = 0;
            using var client = new HttpClient(
                new Handler(async request =>
                {
                    if (request.RequestUri!.AbsolutePath.EndsWith("current-summoner"))
                        return Json(new JObject { ["summonerId"] = 7 });
                    if (request.RequestUri.AbsolutePath != "/lol-item-sets/v1/item-sets/7/sets")
                        throw new Exception("Wrong item-set account path");
                    if (request.Method == HttpMethod.Get)
                        return Json(scenario == "malformed" ? new JObject() : document);
                    if (request.Method != HttpMethod.Put)
                        throw new Exception("Unexpected item-set write");
                    writes++;
                    var payload = JObject.Parse(await request.Content!.ReadAsStringAsync());
                    if (!JToken.DeepEquals(payload["itemSets"]?[0], personal) || (int?)payload["accountId"] != 123)
                        throw new Exception("Personal sets and account metadata must be preserved");
                    if (scenario == "rejected")
                        return new(HttpStatusCode.Forbidden);
                    if (scenario == "success")
                        document = payload;
                    return Json(new JObject());
                })
            )
            {
                BaseAddress = new Uri("https://test.invalid"),
            };
            bool success = false;
            try
            {
                await OpggItemSetService.SaveAsync(client, itemSet, CancellationToken.None);
                success = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpRequestException)
            { }
            if (success != (scenario == "success"))
                throw new Exception("Item-set result must reflect verified save");
            if (scenario == "malformed" && writes != 0)
                throw new Exception("Malformed data must not be overwritten");
            if (success)
            {
                await OpggItemSetService.SaveAsync(client, alternate, CancellationToken.None);
                if (((JArray)document["itemSets"]!).Count != 2)
                    throw new Exception("Reapplying must not accumulate item sets");
                await OpggItemSetService.SaveAsync(client, mayhem, CancellationToken.None);
                await OpggItemSetService.SaveAsync(client, mayhem, CancellationToken.None);
                if (
                    ((JArray)document["itemSets"]!).Count != 3
                    || !JToken.DeepEquals(document["itemSets"]?[1], alternate)
                )
                    throw new Exception(
                        "arammeta reapplication preserves both personal and OP.GG sets without duplicates"
                    );
            }
        }
        Console.WriteLine(
            "PASS: equipment selection, later subtraction, quantities, personal-set preservation and verified saves."
        );
    }

    private static HttpResponseMessage Json(JObject value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request);
    }
}
