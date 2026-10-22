using System.Net;
using System.Reflection;
using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class AramMetaChecks
{
    private static JObject Fixture(string name) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta", name + ".json")));

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => send(r, t);
    }

    private static HttpResponseMessage Json(JObject value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };

    public static async Task RunAsync()
    {
        CheckParsing();
        await CheckMemoryCacheAsync();
        await CheckCancellationAsync();
        await CheckVersionConsistencyAsync();
        await CheckConditionalRequestsAsync();
        await CheckDiskCacheAsync();
        Check(
            Newtonsoft.Json.JsonConvert.DeserializeObject<LeagueConfig>("{}")!.AramMetaCacheMinutes == 1440,
            "Existing settings default to one day"
        );
        await CheckProgressiveLoadAsync();
        await SharedCacheChecks();
        Console.WriteLine("PASS: arammeta models, cache, cancellation, version consistency.");
    }

    private static void CheckParsing()
    {
        var index = AramMetaIndex.Parse(Fixture("index"));
        var ahri = index.Champions.Single(c => c.Id == 103);
        Check(
            index.Patch == "16.17" && ahri.WinRate != ahri.RawWinRate && ahri.Tier == "T1",
            "arammeta preserves adjusted and raw rates separately"
        );
        Check(
            AramMetaIndex.Rate(null) == null && AramMetaIndex.Rate(new JValue(2)) == null,
            "Missing or invalid rates are not zero"
        );
        var spellRows = new[]
        {
            new AramMetaRecommendation(new[] { 4, 7 }, "", "", .51, .8, 100, new()),
            new AramMetaRecommendation(new[] { 4, 21 }, "", "", .6, .2, 20, new()),
            new AramMetaRecommendation(new[] { 4 }, "", "", 1, 1, 10, new()),
        };
        var highlights = AramMetaRecommendation.SpellHighlights(spellRows);
        Check(
            ReferenceEquals(highlights[0].Entry, spellRows[0]) && ReferenceEquals(highlights[1].Entry, spellRows[1]),
            "Spell highlights independently choose highest pick and win rates, ignoring invalid combinations"
        );
        Check(
            AramMetaRecommendation
                .SpellHighlights(new[] { spellRows[0] with { WinRate = null, PickRate = null } })
                .All(h => h.Entry == null),
            "Missing rates do not fabricate a top recommendation"
        );
        var population = index with
        {
            Champions = Enumerable.Range(1, 20).Select(id => ahri with { Id = id, Games = 50 }).ToArray(),
        };
        Check(
            population.SampleMatches == 100 && population.PickRate(population.Champions[0]) == .5,
            "Pick rate uses match count, not share of participant slots"
        );
        var missing = population with { Champions = new[] { ahri with { Games = null } } };
        Check(
            missing.SampleMatches == null && missing.PickRate(ahri) == null,
            "Incomplete population has no fabricated pick rate"
        );
        Check(
            AramMetaIndex.RelativeRate(.6, .6) == 1
                && AramMetaIndex.RelativeRate(.3, .6) == .5
                && AramMetaIndex.RelativeRate(0, 0) == 0,
            "Win-rate bars normalize to the highest displayed rate"
        );
    }

    private static async Task CheckMemoryCacheAsync()
    {
        var fixture = Fixture("103-augments");
        var index = Fixture("index");
        index["augs"] = fixture["augs"]!.DeepClone();
        var detail = Fixture("103");
        detail["poolAugments"] = fixture["detail"]!["poolAugments"]!.DeepClone();
        int calls = 0;
        bool fail = false;
        using var client = new AramMetaClient(
            new Handler(
                (r, t) =>
                {
                    calls++;
                    if (fail)
                    {
                        throw new HttpRequestException("offline");
                    }

                    return Task.FromResult(
                        Json(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json") ? index : detail)
                    );
                }
            )
        );
        var first = await client.GetAsync(103, false, default);
        Check(calls == 3 && first.Detail != null, "Hero detail verified against current index version");
        first.Detail!.RemoveAll();
        var second = await client.GetAsync(103, false, default);
        Check(calls == 3 && second.Cached && second.Detail!.HasValues, "Cached detail is isolated from consumers");
        fail = true;
        var fallback = await client.GetAsync(103, true, default);
        Check(fallback.Cached && fallback.Detail!.HasValues, "Offline refresh preserves complete cached data");
        Check(
            new[] { "kPrismatic", "kGold", "kSilver" }.Sum(category =>
                AramMetaRecommendation.Read(fallback, 103, category).Length
            ) == 129
                && calls == 4,
            "Complete augment statistics survive offline refresh without a separate augment request"
        );
    }

    private static async Task CheckCancellationAsync()
    {
        using var slow = new AramMetaClient(
            new Handler(
                async (r, t) =>
                {
                    await Task.Delay(5000, t);
                    return Json(Fixture("index"));
                }
            )
        );
        using var cancel = new CancellationTokenSource(20);
        bool cancelled = false;
        try
        {
            await slow.GetAsync(0, false, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check(cancelled, "Superseded loads cancel");
    }

    private static async Task CheckVersionConsistencyAsync()
    {
        int indices = 0,
            details = 0;
        using var changing = new AramMetaClient(
            new Handler(
                (r, t) =>
                {
                    if (r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))
                    {
                        var data = Fixture("index");
                        data["detailVersion"] = ++indices == 1 ? "old" : "new";
                        return Task.FromResult(Json(data));
                    }
                    details++;
                    return Task.FromResult(Json(Fixture("103")));
                }
            )
        );
        var updated = await changing.GetAsync(103, false, default);
        Check(details == 2 && updated.Index.Version == "new", "Version change retries detail against new snapshot");
    }

    private static async Task CheckConditionalRequestsAsync()
    {
        int indexBodies = 0,
            notModified = 0;
        string version = "one";
        using var conditional = new AramMetaClient(
            new Handler(
                (r, t) =>
                {
                    if (!r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))
                    {
                        return Task.FromResult(Json(Fixture("103")));
                    }

                    var tag = "\"" + version + "\"";
                    if (r.Headers.IfNoneMatch.Any(v => v.Tag == tag))
                    {
                        notModified++;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
                    }
                    indexBodies++;
                    var data = Fixture("index");
                    data["detailVersion"] = version;
                    var response = Json(data);
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(tag);
                    return Task.FromResult(response);
                }
            )
        );
        await conditional.GetAsync(103, false, default);
        await conditional.GetAsync(157, false, default);
        Check(
            indexBodies == 1 && notModified == 2,
            "Opening new heroes validates ETag without downloading another index body"
        );
        version = "two";
        var changed = await conditional.GetAsync(103, true, default);
        Check(
            changed.Index.Version == "two" && indexBodies == 2 && notModified == 3,
            "Conditional index refresh still detects new versions"
        );
    }

    private static async Task CheckDiskCacheAsync()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), "arammeta-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await PersistCacheAsync(cacheFile);
            await CheckDiskExpiryAsync(cacheFile);
            await File.WriteAllTextAsync(cacheFile, "broken json");
            using var recovery = new AramMetaClient(
                new Handler((r, t) => Task.FromResult(Json(Fixture("index")))),
                cacheFile
            );
            var recovered = await recovery.GetAsync(0, false, default);
            await recovery.FlushAsync();
            Check(
                recovered.Index.Champions.Length == 3
                    && JObject.Parse(await File.ReadAllTextAsync(cacheFile))["schema"]?.Value<int>() == 1,
                "Corrupt cache is replaced with a valid response"
            );
        }
        finally
        {
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
    }

    private static async Task PersistCacheAsync(string cacheFile)
    {
        using (
            var writer = new AramMetaClient(
                new Handler(
                    (r, t) =>
                    {
                        var response = Json(
                            Fixture(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json") ? "index" : "103")
                        );
                        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"disk\"");
                        return Task.FromResult(response);
                    }
                ),
                cacheFile
            )
        )
        {
            await writer.GetAsync(103, false, default);
        }
    }

    private static async Task CheckDiskExpiryAsync(string cacheFile)
    {
        Check(File.Exists(cacheFile), "Statistics snapshot is persisted");
        int requests = 0;
        var lifetime = TimeSpan.FromHours(1);
        using (
            var reader = new AramMetaClient(
                new Handler(
                    (r, t) =>
                    {
                        requests++;
                        Check(
                            r.Headers.IfNoneMatch.Any(v => v.Tag == "\"disk\""),
                            "Disk cache restores HTTP validators"
                        );
                        throw new HttpRequestException("offline");
                    }
                ),
                cacheFile,
                () => lifetime
            )
        )
        {
            var restored = await reader.GetAsync(103, false, default);
            Check(
                requests == 0 && restored.Cached && restored.Detail != null,
                "Restart reuses unexpired statistics without network"
            );
            Check(reader.IsCurrent(restored, 103), "Fresh visible pages can skip cache copying");
            lifetime = TimeSpan.Zero;
            Check(!reader.IsCurrent(restored, 103), "Changed expiry forces visible pages to reload");
            var stale = await reader.GetAsync(103, false, default);
            Check(
                requests == 1 && stale.UpdateFailed && stale.FetchedAt == restored.FetchedAt,
                "Lifetime changes apply immediately and failed refresh does not renew stale data"
            );
            lifetime = TimeSpan.FromHours(1);
            await reader.GetAsync(103, true, default);
            Check(requests == 2, "Manual refresh bypasses fresh disk data");
        }
    }

    private static async Task CheckProgressiveLoadAsync()
    {
        var detailStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDetail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AramMetaPage? preview = null;
        using var progressive = new AramMetaClient(
            new Handler(
                async (r, t) =>
                {
                    if (r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))
                    {
                        return Json(Fixture("index"));
                    }

                    detailStarted.TrySetResult();
                    await releaseDetail.Task.WaitAsync(t);
                    return Json(Fixture("103"));
                }
            )
        );
        var loading = progressive.GetAsync(103, false, default, page => preview = page);
        await detailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(
            preview != null
                && preview.Detail == null
                && preview.Index.Champions.Any(c => c.Id == 103)
                && !loading.IsCompleted,
            "Hero summary is delivered before a slow detail response"
        );
        releaseDetail.SetResult();
        Check((await loading).Detail != null, "Full detail follows the preview");
        int repeatedPreviews = 0;
        await progressive.GetAsync(103, false, default, _ => repeatedPreviews++);
        Check(repeatedPreviews == 0, "Cached heroes render once without a loading preview");
    }

    private static async Task SharedCacheChecks()
    {
        var path = Path.Combine(Path.GetTempPath(), "arammeta-shared-" + Guid.NewGuid().ToString("N") + ".json");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        Handler Network() =>
            new(
                async (r, t) =>
                {
                    Interlocked.Increment(ref requests);
                    if (r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))
                    {
                        return Json(Fixture("index"));
                    }

                    started.TrySetResult();
                    await release.Task.WaitAsync(t);
                    return Json(Fixture("103"));
                }
            );
        try
        {
            using (var first = new AramMetaClient(Network(), path))
            using (
                var second = new AramMetaClient(
                    Network(),
                    Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path))
                )
            )
            {
                var loading = first.GetAsync(103, false, default);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await CheckSharedWaitCancellationAsync(second);
                var duplicate = second.GetAsync(103, false, default);
                release.TrySetResult();
                await Task.WhenAll(loading, duplicate);
                Check(
                    requests == 3 && (await duplicate).Cached,
                    "Concurrent pages share one hero download and normalized cache path"
                );
                await second.GetAsync(157, false, default);
                Check(requests == 5, "A second hero reuses the shared index");
                first.Dispose();
                var retained = await second.GetAsync(103, false, default);
                Check(requests == 5 && retained.Detail != null, "Closing one page preserves the other page's cache");
                await second.FlushAsync();
                var disk = JObject.Parse(await File.ReadAllTextAsync(path));
                Check(
                    ((JObject)disk["details"]!).Count == 2,
                    "Shared disk snapshot retains details fetched by both pages"
                );
            }
            await CheckSharedRestartAsync(path);
        }
        finally
        {
            release.TrySetResult();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task CheckSharedWaitCancellationAsync(AramMetaClient second)
    {
        using var cancel = new CancellationTokenSource();
        var waiting = second.GetAsync(157, false, cancel.Token);
        await cancel.CancelAsync();
        bool cancelled = false;
        try
        {
            await waiting;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check(cancelled, "Cancelling a waiting page does not cancel another page's request");
    }

    private static async Task CheckSharedRestartAsync(string path)
    {
        int offlineRequests = 0;
        using var restarted = new AramMetaClient(
            new Handler(
                (r, t) =>
                {
                    offlineRequests++;
                    throw new HttpRequestException("offline");
                }
            ),
            path
        );
        foreach (var hero in new[] { 103, 157 })
        {
            Check(
                (await restarted.GetAsync(hero, false, default)).Detail != null,
                "Restart restores every shared hero detail"
            );
        }

        Check(offlineRequests == 0, "Restarted pages use persisted statistics without network");
    }

    public static void Render()
    {
        var client = new AramMetaClient(
            new Handler(
                (r, t) =>
                    Task.FromResult(
                        Json(Fixture(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json") ? "index" : "103"))
                    )
            )
        );
        int imageRequests = 0;
        using var images = new HttpClient(
            new Handler(
                (r, t) =>
                {
                    imageRequests++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }
            )
        );
        var type = typeof(PlayerCardControl).Assembly.GetType("League.Controls.AramMetaView")!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var view = (Control)Activator.CreateInstance(type, flags, null, new object[] { client, images }, null)!;
        CheckLocalImageCache(view, type, flags, () => imageRequests);
        using var form = new Form { ClientSize = new Size(1060, 800) };
        form.Controls.Add(view);
        form.Show();
        Application.DoEvents();
        CheckRankings(view, form, type, flags);
        var (body, tabs) = CheckDetailCompletion(view, type, flags);
        CheckAugmentNavigation(view, form, type, flags, body, tabs);
        CheckTravel(view, form, type, flags);
        form.Close();
        Console.WriteLine("PASS: arammeta navigation, filtering, desktop layouts.");
    }

    private static void CheckLocalImageCache(Control view, Type type, BindingFlags flags, Func<int> imageRequests)
    {
        var resourceName = "arammeta-test-" + Guid.NewGuid().ToString("N");
        var champion = new Champion
        {
            Id = 999999,
            Name = resourceName,
            Alias = resourceName,
        };
        var cacheFile = Path.Combine(Application.StartupPath, "Assets", "champion", resourceName + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        using (var source = new Bitmap(16, 16))
        {
            source.SetPixel(0, 0, Color.Red);
            source.Save(cacheFile);
        }
        League.FormMain.Globals.resLoading.Champions.Champions.Add(champion);
        try
        {
            CheckCachedImages(view, type, flags, champion, resourceName, imageRequests);
            CheckIdOnlyResources(view, type, flags, champion, imageRequests);
        }
        finally
        {
            League.FormMain.Globals.resLoading.Champions.Champions.Remove(champion);
            File.Delete(cacheFile);
        }
    }

    private static void CheckCachedImages(
        Control view,
        Type type,
        BindingFlags flags,
        Champion champion,
        string resourceName,
        Func<int> imageRequests
    )
    {
        var task =
            (Task<Image?>)
                type.GetMethod("ImageAsync", flags)!
                    .Invoke(
                        view,
                        new object[]
                        {
                            "https://arammeta.com/assets/icons/" + resourceName + ".png",
                            "champion",
                            champion.Id,
                        }
                    )!;
        StatisticsCheckSupport.PumpUntil(() => task.IsCompleted);
        var cached = task.GetAwaiter().GetResult();
        Check(
            cached is Bitmap bitmap && bitmap.GetPixel(0, 0).R == 255 && imageRequests() == 0,
            "arammeta reuses match-history disk images without a public image request"
        );
        var localTask =
            (Task<Image?>)
                type.GetMethod("ImageAsync", flags)!.Invoke(view, new object?[] { null, "champion", champion.Id })!;
        StatisticsCheckSupport.PumpUntil(() => localTask.IsCompleted);
        Check(
            localTask.GetAwaiter().GetResult() is Bitmap localBitmap
                && localBitmap.GetPixel(0, 0).R == 255
                && imageRequests() == 0,
            "ID-only recommendations load local images without a remote URL"
        );
    }

    private static void CheckIdOnlyResources(
        Control view,
        Type type,
        BindingFlags flags,
        Champion champion,
        Func<int> imageRequests
    )
    {
        using var iconHost = new Panel();
        var icon = (PictureBox)
            type.GetMethod("Icon", flags)!
                .Invoke(
                    view,
                    new object?[] { iconHost, null, "test", CancellationToken.None, 36, "champion", champion.Id }
                )!;
        Application.DoEvents();
        Check(icon.Image != null, "Recommendation picture boxes display ID-only resources");
        var missing =
            (Task<Image?>)
                type.GetMethod("ImageAsync", flags)!.Invoke(view, new object?[] { null, "item", champion.Id })!;
        StatisticsCheckSupport.PumpUntil(() => missing.IsCompleted);
        Check(
            missing.GetAwaiter().GetResult() == null && imageRequests() == 0,
            "Resource cache separates categories with the same numeric ID"
        );
    }

    private static void CheckRankings(Control view, Form form, Type type, BindingFlags flags)
    {
        RunPageMethod(view, type, flags, "LoadAsync", false);
        var grid = (DataGridView)type.GetField("_grid", flags)!.GetValue(view)!;
        var search = (TextBox)type.GetField("_search", flags)!.GetValue(view)!;
        Check(
            grid.Rows.Count == 3,
            "Rankings display complete fixture (rows="
                + grid.Rows.Count
                + ", search="
                + search.Text
                + ", context="
                + SynchronizationContext.Current?.GetType().Name
                + "): "
                + ((Label)type.GetField("_status", flags)!.GetValue(view)!).Text
        );
        Check(
            grid.Rows.Cast<DataGridViewRow>().All(r => r.Height == 56),
            "Batched ranking rows retain space for portraits, badges and win-rate bars"
        );
        SavePreview(form, "arammeta-rankings", 1060, 800);
        search.Text = "Ahri";
        Check(grid.Rows.Count == 1, "Champion search filters aliases");
        Check(grid.Rows[0].Height == 56, "Filtering retains ranking row height");
    }

    private static (Control Body, FlowLayoutPanel Tabs) CheckDetailCompletion(
        Control view,
        Type type,
        BindingFlags flags
    )
    {
        RunPageMethod(view, type, flags, "NavigateAsync", 103);
        var detailPanel = (FlowLayoutPanel)type.GetField("_detail", flags)!.GetValue(view)!;
        var complete = (AramMetaPage)type.GetField("_page", flags)!.GetValue(view)!;
        var retainedSummary = detailPanel.Controls[0];
        RunPageMethod(view, type, flags, "LoadAsync", false);
        Check(
            ReferenceEquals(retainedSummary, detailPanel.Controls[0]),
            "Unchanged cached detail retains controls instead of rebuilding"
        );
        RunPageMethod(view, type, flags, "LoadAsync", true);
        Check(
            !ReferenceEquals(retainedSummary, detailPanel.Controls[0]),
            "Manual refresh still rebuilds updated detail"
        );
        var render = type.GetMethod("RenderDetail", flags)!;
        render.Invoke(view, new object[] { complete with { Detail = null }, 103, CancellationToken.None, false });
        var summary = detailPanel.Controls[0];
        var tabs = detailPanel.Controls[1].Controls.OfType<FlowLayoutPanel>().Single();
        ((Button)tabs.Controls[2]).PerformClick();
        render.Invoke(view, new object[] { complete, 103, CancellationToken.None, true });
        Check(
            ReferenceEquals(summary, detailPanel.Controls[0]) && detailPanel.Controls.Count == 3,
            "Equipment completion preserves summary controls and replaces only placeholders"
        );
        Check(
            (int)type.GetField("_detailTab", flags)!.GetValue(view)! == 2,
            "Detail completion preserves the selected equipment tab"
        );
        ((Button)tabs.Controls[2]).PerformClick();
        Check(
            (int)type.GetField("_detailTab", flags)!.GetValue(view)! == 2,
            "Equipment and spells share a dedicated tab"
        );
        return (detailPanel.Controls[2], tabs);
    }

    private static void CheckAugmentNavigation(
        Control view,
        Form form,
        Type type,
        BindingFlags flags,
        Control body,
        FlowLayoutPanel tabs
    )
    {
        ((Button)tabs.Controls[1]).PerformClick();
        ((Button)body.Controls.OfType<FlowLayoutPanel>().First().Controls[1]).PerformClick();
        Check((int)type.GetField("_rarity", flags)!.GetValue(view)! == 1, "Rarity buttons switch recommendations");
        body.Controls.OfType<FlowLayoutPanel>().SelectMany(c => c.Controls.OfType<ComboBox>()).Single().SelectedIndex =
            1;
        Check((int)type.GetField("_augmentSort", flags)!.GetValue(view)! == 1, "Augment sorting remains available");
        SavePreview(form, "arammeta-detail", 1060, 800);
        // The augments are a grid of cards; a click shows the description that a hover would.
        FlowLayoutPanel Grid() =>
            body.Controls.OfType<FlowLayoutPanel>().First(c => c.Tag is string tag && tag == "augment-grid");
        int goldCards = Grid().Controls.OfType<Panel>().Count();
        Check(goldCards > 0, "The augment tab lists the chosen rarity as cards");
        typeof(Control)
            .GetMethod("OnClick", flags)!
            .Invoke(Grid().Controls.OfType<Panel>().First(), new object[] { EventArgs.Empty });
        SavePreview(form, "arammeta-comparison-selected", 1060, 800);
        ((Button)body.Controls.OfType<FlowLayoutPanel>().First().Controls[3]).PerformClick();
        Check(
            (int)type.GetField("_rarity", flags)!.GetValue(view)! == 3
                && Grid().Controls.OfType<Panel>().Count() >= goldCards,
            "The all-rarities choice shows every rarity together"
        );
        SavePreview(form, "arammeta-comparison-all", 1060, 800);
        ((Button)tabs.Controls[0]).PerformClick();
        SavePreview(form, "arammeta-overview", 1060, 800);
        ((Button)tabs.Controls[2]).PerformClick();
        SavePreview(form, "arammeta-build-comparison", 1060, 800);
        ((Button)tabs.Controls[1]).PerformClick();
    }

    private static void CheckTravel(Control view, Form form, Type type, BindingFlags flags)
    {
        var search = (TextBox)type.GetField("_search", flags)!.GetValue(view)!;
        var grid = (DataGridView)type.GetField("_grid", flags)!.GetValue(view)!;
        RunPageMethod(view, type, flags, "TravelAsync", -1);
        Check(search.Text == "Ahri" && grid.Rows.Count == 1 && grid.Visible, "Back restores ranking search");
        RunPageMethod(view, type, flags, "TravelAsync", 1);
        Check((int)type.GetField("_champion", flags)!.GetValue(view)! == 103, "Forward restores hero");
        form.ClientSize = new Size(660, 700);
        Application.DoEvents();
        SavePreview(form, "arammeta-narrow", 660, 700);
    }

    private static void RunPageMethod(Control view, Type type, BindingFlags flags, string method, params object[] args)
    {
        var task = (Task)type.GetMethod(method, flags)!.Invoke(view, args)!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(5);
            if (DateTime.UtcNow > deadline)
            {
                throw new Exception(
                    "Timed out: "
                        + method
                        + " / "
                        + task.Status
                        + " / "
                        + ((Label)type.GetField("_status", flags)!.GetValue(view)!).Text
                        + " / "
                        + SynchronizationContext.Current?.GetType().Name
                );
            }
        }
        task.GetAwaiter().GetResult();
        Application.DoEvents();
    }

    private static void SavePreview(Form form, string file, int width, int height)
    {
        using var bmp = new Bitmap(width, height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, width, height));
        bmp.Save(Path.Combine(AppContext.BaseDirectory, file + ".png"));
    }
}
