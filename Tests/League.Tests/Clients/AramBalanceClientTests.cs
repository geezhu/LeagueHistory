using System.Net;
using League.Clients;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The ARAM balance feed: the pure row lookup and description, the alias mapping, the unit every adjustment is
/// rendered with, the one download five readers share, the lifetime that expires it, the failure backoff, the disk
/// snapshot that survives a restart and cancellation. Every request goes to a stub handler and every file lives in a
/// temporary folder, so no request leaves the process and the user's own cache is never touched. The expected values
/// come from the checks in Tests/StatisticsChecks/AramBalanceChecks.cs, which this class replaces.
/// </summary>
public class AramBalanceClientTests
{
    /// <summary>The address the client downloads the feed from.</summary>
    private const string Url = "https://www.aramonly.com/page-data/aram-changes/page-data.json";

    private const string Patch = "test";

    private const string Timestamp = "2026-09-03T10:13:06.697856+00:00";

    /// <summary>
    /// The description of the sample row, in the order the source declares the fields. The source joins its lines
    /// with a plain newline, so every expected description states its newlines instead of relying on the source file's
    /// own line endings.
    /// </summary>
    private const string SampleDescription = "造成伤害 +10%\n受到伤害 -10%\n技能急速 +10\n攻速成长 +2.5%";

    private const string Unmatched = "暂未匹配到该英雄的平衡数据。";

    private const string NoAdjustment = "来源列出的各项属性均无额外调整。";

    private const string Gap = "部分调整项暂无数据。";

    private const string Incomplete = "平衡数据结构不完整。";

    private const string Unavailable = "普通大乱斗平衡\n暂时无法读取数据，稍后悬浮重试。";

    private const string StaleMarker = "（更新失败，使用旧缓存）";

    private const string Tail = "\n来源版本可能落后于客户端；不代表海克斯大乱斗。";

    [Fact]
    public void Nodes_TheSampleFeed_ReturnsTheRowsOfTheFeed()
    {
        JArray nodes = AramBalanceClient.Nodes(SampleFeed());

        JToken row = Assert.Single(nodes);
        Assert.Equal("Nunu & Willump", (string?)row["champion"]?["name"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"result\":{\"data\":{}}}")]
    [InlineData("{\"result\":{\"data\":{\"allAramModifiersJson\":{\"nodes\":[]}}}}")]
    [InlineData("{\"result\":{\"data\":{\"allAramModifiersJson\":{\"nodes\":\"Nunu\"}}}}")]
    [InlineData("{\"result\":{\"data\":{\"allAramModifiersJson\":{\"nodes\":{\"champion\":\"Nunu\"}}}}}")]
    public void Nodes_AFeedWithoutARowList_RefusesToDescribeIt(string json)
    {
        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            AramBalanceClient.Nodes(JObject.Parse(json))
        );

        Assert.Equal(Incomplete, failure.Message);
    }

    [Fact]
    public void Describe_TheSampleRow_RendersEveryAdjustmentWithItsOwnUnit()
    {
        string text = AramBalanceClient.Describe(SampleFeed(), "Nunu");

        Assert.Equal(SampleDescription, text);
    }

    [Fact]
    public void Describe_AChampionWithEveryAdjustment_ListsThemInTheDeclaredOrder()
    {
        JObject row = Row("Annie");
        row["aramDamageDealt"] = 1.1;
        row["aramDamageTaken"] = 0.9;
        row["aramHealing"] = 1.1;
        row["aramShielding"] = 0.9;
        row["aramTenacity"] = 1.1;
        row["aramAbilityHaste"] = 20;
        row["aramAttackSpeed"] = 1.025;
        row["aramEnergyRegen"] = 1.5;

        string text = AramBalanceClient.Describe(Feed(row), "Annie");

        Assert.Equal(
            "造成伤害 +10%\n受到伤害 -10%\n治疗效果 +10%\n护盾效果 -10%\n韧性 +10%\n"
                + "技能急速 +20\n攻速成长 +2.5%\n能量回复 +50%",
            text
        );
    }

    [Theory]
    [InlineData("Nunu")]
    [InlineData("nunu")]
    [InlineData("NUNU")]
    [InlineData("Nunu & Willump")]
    [InlineData("Nunu&Willump")]
    [InlineData("nunuandwillump")]
    [InlineData(" Nunu & Willump ")]
    public void Describe_TheFormsOfOneAlias_AllMatchTheSameRow(string alias)
    {
        string text = AramBalanceClient.Describe(SampleFeed(), alias);

        Assert.Equal(SampleDescription, text);
    }

    [Theory]
    [InlineData("MonkeyKing", "Wukong")]
    [InlineData("Monkey King", "Wukong")]
    [InlineData("Wukong", "Wukong")]
    [InlineData("Renata", "Renata Glasc")]
    [InlineData("RenataGlasc", "Renata Glasc")]
    [InlineData("Renata Glasc", "Renata Glasc")]
    public void Describe_AShortAliasOfAChampion_FindsTheRowOfItsFullName(string alias, string name)
    {
        JObject row = Row(name);
        row["aramDamageDealt"] = 1.1;

        string text = AramBalanceClient.Describe(Feed(row), alias);

        Assert.Equal("造成伤害 +10%", text);
    }

    [Fact]
    public void Describe_ADisplayNameThatDoesNotMatch_TheSanitizedNameDoes()
    {
        JObject row = Row("Nunu & Willump", "NunuWillump");
        row["aramDamageTaken"] = 1.1;

        string text = AramBalanceClient.Describe(Feed(row), "Nunu Willump");

        Assert.Equal("受到伤害 +10%", text);
    }

    [Fact]
    public void Describe_ARowWhoseChampionCannotBeRead_IsSkippedForTheNextRow()
    {
        JObject championIsNull = Row("Nunu");
        championIsNull["champion"] = JValue.CreateNull();
        JObject championIsText = Row("Nunu");
        championIsText["champion"] = "invalid";
        JObject nameIsAnObject = Row("Nunu");
        nameIsAnObject["champion"] = new JObject { ["name"] = new JObject() };

        string text = AramBalanceClient.Describe(
            Feed(championIsNull, championIsText, nameIsAnObject, SampleRow()),
            "Nunu"
        );

        Assert.Equal(SampleDescription, text);
    }

    [Theory]
    [InlineData("Annie")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void Describe_AnAliasTheFeedDoesNotHold_DoesNotClaimThereAreNoAdjustments(string alias)
    {
        string text = AramBalanceClient.Describe(SampleFeed(), alias);

        Assert.Equal(Unmatched, text);
    }

    [Fact]
    public void Describe_AnAliasWithoutLettersOrDigits_MatchesNoRow()
    {
        JObject namelessChampion = Row("Nunu");
        namelessChampion["champion"] = new JObject();

        string text = AramBalanceClient.Describe(Feed(namelessChampion), "!!!");

        Assert.Equal(Unmatched, text);
    }

    [Fact]
    public void Describe_ARowWithoutAnyAdjustment_SaysTheSourceListsNone()
    {
        string text = AramBalanceClient.Describe(Feed(Row("Annie")), "Annie");

        Assert.Equal(NoAdjustment, text);
    }

    [Fact]
    public void Describe_ARowMissingOneAdjustment_NotesTheGapAfterTheAdjustments()
    {
        JObject row = SampleRow();
        row.Remove("aramTenacity");

        string text = AramBalanceClient.Describe(Feed(row), "Nunu");

        Assert.Equal(SampleDescription + "\n" + Gap, text);
    }

    [Theory]
    [InlineData("aramHealing", "null")]
    [InlineData("aramShielding", "\"10%\"")]
    [InlineData("aramTenacity", "true")]
    [InlineData("aramEnergyRegen", "[]")]
    public void Describe_ANeutralAdjustmentThatIsNotANumber_NotesTheGap(string field, string value)
    {
        JObject row = SampleRow();
        row[field] = JToken.Parse(value);

        string text = AramBalanceClient.Describe(Feed(row), "Nunu");

        Assert.Equal(SampleDescription + "\n" + Gap, text);
    }

    [Theory]
    [InlineData("aramDamageDealt", "1.0000000001")]
    [InlineData("aramTenacity", "1.0000000001")]
    [InlineData("aramEnergyRegen", "0.9999999999")]
    [InlineData("aramAbilityHaste", "0.0000001")]
    public void Describe_AnAdjustmentBelowTheTolerance_IsLeftOut(string field, string value)
    {
        JObject row = Row("Annie");
        row[field] = JToken.Parse(value);

        string text = AramBalanceClient.Describe(Feed(row), "Annie");

        Assert.Equal(NoAdjustment, text);
    }

    [Theory]
    [InlineData(1.5, "造成伤害 +50%")]
    [InlineData(0.5, "造成伤害 -50%")]
    [InlineData(1.005, "造成伤害 +0.5%")]
    [InlineData(1.123456, "造成伤害 +12.35%")]
    public void Describe_APercentageAdjustment_IsRenderedSignedAndScaled(double value, string expected)
    {
        JObject row = Row("Annie");
        row["aramDamageDealt"] = value;

        string text = AramBalanceClient.Describe(Feed(row), "Annie");

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(10.0, "技能急速 +10")]
    [InlineData(12.5, "技能急速 +12.5")]
    [InlineData(-15.0, "技能急速 -15")]
    public void Describe_AnAbilityHasteAdjustment_IsFlatWithoutAPercentSign(double value, string expected)
    {
        JObject row = Row("Annie");
        row["aramAbilityHaste"] = value;

        string text = AramBalanceClient.Describe(Feed(row), "Annie");

        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task GetAsync_TheFirstRead_DownloadsTheFeedAndDescribesTheAlias()
    {
        using var folder = new TempFolder("balance-download");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(HttpMethod.Get, stub.LastMethod);
        Assert.Equal(Url, stub.LastRequest?.AbsoluteUri);
        Assert.Equal(1, stub.Requests);
        Assert.StartsWith(
            "普通大乱斗平衡\n" + SampleDescription + "\n\n来源：aramonly · 版本 " + Patch + "\n来源更新时间：",
            summary
        );
        // The feed dates its update, and the client hands that token to the caller as it read it; a date token renders
        // with the culture and time zone of the machine, so only the line itself is asserted here.
        Assert.Contains("\n缓存时间：", summary);
        Assert.EndsWith(Tail, summary);
    }

    [Fact]
    public async Task GetAsync_AnUpdateTimeTheSourceGivesAsText_IsEchoedOnItsOwnLine()
    {
        using var folder = new TempFolder("balance-updated");
        JObject feed = Feed(SampleRow());
        ((JObject)feed["result"]!["data"]!["aramModifiersJson"]!)["timestamp"] = "刚刚更新";
        var stub = new BalanceStub { Body = feed.ToString() };
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Contains("来源更新时间：刚刚更新\n缓存时间：", summary);
    }

    [Fact]
    public async Task GetAsync_TheFirstRead_WritesTheSnapshotItDownloaded()
    {
        using var folder = new TempFolder("balance-snapshot");
        string path = folder.File("balance.json");
        using var client = new AramBalanceClient(new BalanceStub(), path);
        DateTime before = DateTime.UtcNow;

        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        var snapshot = JObject.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(Patch, (string?)snapshot["data"]?["result"]?["data"]?["patchVersionJson"]?["patchVersion"]);
        Assert.InRange(snapshot["fetched"]!.Value<DateTime>(), before, DateTime.UtcNow);
        Assert.Empty(Directory.GetFiles(folder.Root, "*.tmp"));
    }

    [Fact]
    public async Task GetAsync_ASecondReadWithinTheLifetime_DoesNotDownloadAgain()
    {
        using var folder = new TempFolder("balance-fresh");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(1, stub.Requests);
    }

    [Fact]
    public async Task GetAsync_ASecondReadPastTheLifetime_DownloadsAgain()
    {
        using var folder = new TempFolder("balance-expired");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        await client.GetAsync("Nunu", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, stub.Requests);
    }

    [Fact]
    public async Task GetAsync_ADiskSnapshotOlderThanTheLifetime_IsDownloadedAgain()
    {
        using var folder = new TempFolder("balance-old");
        string path = folder.File("balance.json");
        JObject saved = Row("Nunu & Willump");
        saved["aramDamageDealt"] = 1.5;
        await WriteSnapshotAsync(path, DateTime.UtcNow.AddDays(-2), Feed(saved));
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, path);

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(1, stub.Requests);
        Assert.Contains("造成伤害 +10%", summary);
        Assert.DoesNotContain("+50%", summary);
    }

    [Fact]
    public async Task GetAsync_ARestartedClient_ReadsTheSnapshotWithoutADownload()
    {
        using var folder = new TempFolder("balance-restart");
        string path = folder.File("balance.json");
        using (var writer = new AramBalanceClient(new BalanceStub(), path))
        {
            await writer.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);
        }

        var stub = new BalanceStub { Fail = true };
        using var reader = new AramBalanceClient(stub, path);

        string summary = await reader.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(0, stub.Requests);
        Assert.Contains(SampleDescription, summary);
        Assert.DoesNotContain(StaleMarker, summary);
    }

    [Fact]
    public async Task GetAsync_ADiskSnapshotDatedInTheFuture_IsDownloadedAgain()
    {
        using var folder = new TempFolder("balance-future");
        string path = folder.File("balance.json");
        await WriteSnapshotAsync(path, DateTime.UtcNow.AddHours(1), Feed(Row("Nunu & Willump")));
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, path);

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(1, stub.Requests);
        Assert.Contains(SampleDescription, summary);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{}")]
    [InlineData("{\"data\":{\"result\":{\"data\":{\"allAramModifiersJson\":{\"nodes\":[]}}}}}")]
    [InlineData(
        "{\"data\":{\"result\":{\"data\":{\"allAramModifiersJson\":{\"nodes\":[{\"champion\":{\"name\":\"Nunu & Willump\"}}]}}}}}"
    )]
    public async Task GetAsync_ADiskSnapshotThatCannotBeTrusted_IsReplacedByADownload(string snapshot)
    {
        using var folder = new TempFolder("balance-untrusted");
        string path = folder.File("balance.json");
        await File.WriteAllTextAsync(path, snapshot);
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, path);

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(1, stub.Requests);
        Assert.Contains(SampleDescription, summary);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{}")]
    [InlineData("{\"result\":{\"data\":{\"allAramModifiersJson\":{\"nodes\":[]}}}}")]
    public async Task GetAsync_ASourceResponseWithoutARowList_ReportsThatTheSourceIsUnreadable(string body)
    {
        using var folder = new TempFolder("balance-badresponse");
        string path = folder.File("balance.json");
        var stub = new BalanceStub { Body = body };
        using var client = new AramBalanceClient(stub, path);

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(Unavailable, summary);
        Assert.Equal(1, stub.Requests);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task GetAsync_ASourceWithoutAPatchOrTimestamp_RendersUnknownForBoth()
    {
        using var folder = new TempFolder("balance-noversion");
        JObject feed = Feed(SampleRow());
        var data = (JObject)feed["result"]!["data"]!;
        data.Remove("patchVersionJson");
        data.Remove("aramModifiersJson");
        var stub = new BalanceStub { Body = feed.ToString() };
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Contains("来源：aramonly · 版本 未知\n来源更新时间：未知\n", summary);
        Assert.Contains(SampleDescription, summary);
    }

    [Fact]
    public async Task GetAsync_AnAliasTheFeedDoesNotHold_SaysItDidNotMatchInsteadOfNoAdjustments()
    {
        using var folder = new TempFolder("balance-alias");
        using var client = new AramBalanceClient(new BalanceStub(), folder.File("balance.json"));

        string summary = await client.GetAsync("Annie", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Contains(Unmatched, summary);
        Assert.DoesNotContain(NoAdjustment, summary);
    }

    [Fact]
    public async Task GetAsync_AClientWithoutACachePath_StillAnswersFromTheSource()
    {
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub);

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(1, stub.Requests);
        Assert.Contains(SampleDescription, summary);
    }

    [Fact]
    public async Task GetAsync_ASourceThatCannotBeReached_ReportsTheRetryMessageWithoutASnapshot()
    {
        using var folder = new TempFolder("balance-offline");
        string path = folder.File("balance.json");
        var stub = new BalanceStub { Fail = true };
        using var client = new AramBalanceClient(stub, path);

        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(Unavailable, summary);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task GetAsync_ASourceThatCannotBeReached_IsNotAskedAgainWithinTheBackoff()
    {
        using var folder = new TempFolder("balance-offline-backoff");
        var stub = new BalanceStub { Fail = true };
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(1, stub.Requests);
    }

    [Fact]
    public async Task GetAsync_ADownloadThatFails_KeepsTheCachedFeedAndMarksItStale()
    {
        using var folder = new TempFolder("balance-stale");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);
        stub.Fail = true;

        string summary = await client.GetAsync("Nunu", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, stub.Requests);
        Assert.Contains(SampleDescription, summary);
        Assert.EndsWith(StaleMarker + Tail, summary);
    }

    [Fact]
    public async Task GetAsync_AfterAFailedDownload_TheBackoffKeepsTheStaleFeedWithoutAskingAgain()
    {
        using var folder = new TempFolder("balance-stale-backoff");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);
        stub.Fail = true;
        await client.GetAsync("Nunu", TimeSpan.Zero, CancellationToken.None);

        string summary = await client.GetAsync("Nunu", TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, stub.Requests);
        Assert.Contains(StaleMarker, summary);
    }

    [Fact]
    public async Task GetAsync_FiveConcurrentReads_ShareOneDownload()
    {
        using var folder = new TempFolder("balance-coalesce");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));

        string[] summaries = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None))
        );

        Assert.Equal(1, stub.Requests);
        Assert.All(summaries, summary => Assert.Contains(SampleDescription, summary));
    }

    [Fact]
    public async Task GetAsync_ACancelledToken_StopsBeforeAnyRequest()
    {
        using var folder = new TempFolder("balance-cancelled");
        var stub = new BalanceStub();
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetAsync("Nunu", TimeSpan.FromDays(1), cancel.Token)
        );

        Assert.Equal(0, stub.Requests);
    }

    [Fact]
    public async Task GetAsync_CancellingADownload_PublishesNothing()
    {
        using var folder = new TempFolder("balance-cancel-download");
        string path = folder.File("balance.json");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new BalanceStub { Gate = gate };
        using var client = new AramBalanceClient(stub, path);
        using var cancel = new CancellationTokenSource();
        Task<string> loading = client.GetAsync("Nunu", TimeSpan.FromDays(1), cancel.Token);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10), cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task GetAsync_ACancelledReadThatWaitedForTheDownload_LeavesTheDownloadAlone()
    {
        using var folder = new TempFolder("balance-cancel-waiter");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new BalanceStub { Gate = gate };
        using var client = new AramBalanceClient(stub, folder.File("balance.json"));
        Task<string> first = client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancel = new CancellationTokenSource();
        Task<string> waiting = client.GetAsync("Nunu", TimeSpan.FromDays(1), cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.SetResult();
        string summary = await first;
        Assert.Equal(1, stub.Requests);
        Assert.Contains(SampleDescription, summary);
    }

    [Fact]
    public async Task GetAsync_ADisposedClient_ReportsThatTheSourceIsUnreadable()
    {
        using var folder = new TempFolder("balance-disposed");
        var client = new AramBalanceClient(new BalanceStub(), folder.File("balance.json"));

        client.Dispose();
        string summary = await client.GetAsync("Nunu", TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Equal(Unavailable, summary);
    }

    /// <summary>The feed of the old check: one sample row inside the envelope of the source.</summary>
    private static JObject SampleFeed() => Feed(SampleRow());

    /// <summary>The sample row: two percentage adjustments, one flat adjustment and one growth value.</summary>
    private static JObject SampleRow()
    {
        JObject row = Row("Nunu & Willump");
        row["aramDamageDealt"] = 1.1;
        row["aramDamageTaken"] = 0.9;
        row["aramAbilityHaste"] = 10;
        row["aramAttackSpeed"] = 1.025;
        return row;
    }

    /// <summary>
    /// A balance row in the shape the source serves: every adjustment neutral, so a test only names the ones its rule
    /// is about.
    /// </summary>
    private static JObject Row(string name, string? sanitizedName = null) =>
        new()
        {
            ["champion"] = new JObject { ["name"] = name, ["sanitizedName"] = sanitizedName ?? name },
            ["aramDamageDealt"] = 1,
            ["aramDamageTaken"] = 1,
            ["aramHealing"] = 1,
            ["aramShielding"] = 1,
            ["aramTenacity"] = 1,
            ["aramAbilityHaste"] = 0,
            ["aramAttackSpeed"] = 1,
            ["aramEnergyRegen"] = 1,
        };

    /// <summary>The envelope around <paramref name="rows"/>, with the patch and timestamp of the old check.</summary>
    private static JObject Feed(params JObject[] rows) =>
        new()
        {
            ["result"] = new JObject
            {
                ["data"] = new JObject
                {
                    ["allAramModifiersJson"] = new JObject { ["nodes"] = new JArray(rows) },
                    ["patchVersionJson"] = new JObject { ["patchVersion"] = Patch },
                    ["aramModifiersJson"] = new JObject { ["timestamp"] = Timestamp },
                },
            },
        };

    /// <summary>Writes the snapshot the client keeps, in the shape it reads it back in.</summary>
    private static async Task WriteSnapshotAsync(string path, DateTime fetched, JObject data) =>
        await File.WriteAllTextAsync(path, new JObject { ["fetched"] = fetched, ["data"] = data }.ToString());

    /// <summary>Answers balance requests from a prepared body, counts them and can hold one open; nothing leaves the process.</summary>
    private sealed class BalanceStub : HttpMessageHandler
    {
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public string Body { get; set; } = SampleFeed().ToString();

        public bool Fail { get; set; }

        /// <summary>Held open to keep one download in flight while the test inspects the cache.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public int Requests => Volatile.Read(ref _requests);

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequest { get; private set; }

        /// <summary>Completed once a request waits on <see cref="Gate"/>.</summary>
        public Task Waiting => _waiting.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Interlocked.Increment(ref _requests);
            LastMethod = request.Method;
            LastRequest = request.RequestUri;
            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            if (Gate != null)
            {
                _waiting.TrySetResult();
                await Gate.Task.WaitAsync(token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
        }
    }
}
