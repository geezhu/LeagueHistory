using System.Reflection;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class MayhemAssistantChecks
{
    public static void Run()
    {
        using var owner = new League.FormMain();
        var type = typeof(League.FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var assistant = (Form)Activator.CreateInstance(type, new object[] { owner })!;
        ((System.Windows.Forms.Timer)type.GetField("_timer", flags)!.GetValue(assistant)!).Stop();
        ((ComboBox)type.GetField("_mode", flags)!.GetValue(assistant)!).SelectedIndex = 3;
        var body = (FlowLayoutPanel)type.GetField("_body", flags)!.GetValue(assistant)!;
        body.Controls.Add(new Panel { Size = new Size(282, 84), Margin = new Padding(0, 0, 0, 4) });
        var page = CreatePage();
        type.GetMethod("AddMayhemBrowser", flags)!
            .Invoke(assistant, new object[] { page, 103, CancellationToken.None, body, 0, 0 });
        CheckBrowser(assistant, body, type, flags);
        CheckOverview(assistant, body, type, flags, page);
        Console.WriteLine("PASS: mayhem tabs, pagination, recommendations, and message-only action label.");
    }

    private static AramMetaPage CreatePage()
    {
        var augments = new JArray(
            Enumerable
                .Range(1, 7)
                .Select(i => new JObject
                {
                    ["id"] = 99000 + i,
                    ["wr"] = .50 + i * .01,
                    ["pick"] = .1,
                    ["g"] = 100,
                })
        );
        var champion = new AramMetaChampion(
            103,
            "Ahri",
            "",
            Array.Empty<string>(),
            .53,
            .52,
            100,
            new JObject
            {
                ["top"] = new JObject
                {
                    ["kPrismatic"] = augments,
                    ["kGold"] = augments.DeepClone(),
                    ["kSilver"] = augments.DeepClone(),
                },
            }
        );
        var index = new AramMetaIndex("16.17", "test", new[] { champion }, new JObject(), new JObject());
        var recommendations = new JArray(
            Enumerable
                .Range(1, 4)
                .Select(i => new JObject
                {
                    ["name_zh"] = "Option " + i,
                    ["wr"] = .52,
                    ["pick"] = .1,
                    ["g"] = 100,
                    ["items"] = new JArray(new JObject { ["id"] = 99100 + i }, new JObject { ["id"] = 99200 + i }),
                })
        );
        var detail = new JObject();
        foreach (string key in new[] { "items", "singleItems", "boots", "spells" })
        {
            detail[key] = new JObject { ["top"] = recommendations.DeepClone() };
        }

        return new AramMetaPage(index, detail, true, DateTime.UtcNow);
    }

    private static void CheckBrowser(Form assistant, FlowLayoutPanel body, Type type, BindingFlags flags)
    {
        assistant.Show();
        Application.DoEvents();
        var action = (Button)type.GetField("_apply", flags)!.GetValue(assistant)!;
        if (!action.Visible || action.Text != "应用符文")
        {
            throw new Exception("Mayhem must retain the requested rune action label");
        }

        var tabs = body.Controls[1];
        var rows = body.Controls[3];
        var pager = body.Controls[4];
        if (rows.Controls.Count != 4)
        {
            throw new Exception("Mayhem must display four augment recommendations per page");
        }
        ((Button)pager.Controls[2]).PerformClick();
        Application.DoEvents();
        if (rows.Controls.Count != 3)
        {
            throw new Exception("Mayhem pagination must expose remaining recommendations");
        }

        foreach (int tab in new[] { 0, 1, 2 })
        {
            ((Button)tabs.Controls[tab]).PerformClick();
            Application.DoEvents();
            if (
                rows.Controls.Count
                != (
                    tab == 0 ? 4
                    : tab == 1 ? 3
                    : 2
                )
            )
            {
                throw new Exception("Mayhem spells retain only the two highlight recommendations");
            }

            using var bitmap = new Bitmap(assistant.Width, assistant.Height);
            assistant.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"assistant-mayhem-tab-{tab}.png"));
        }
    }

    private static void CheckOverview(
        Form assistant,
        FlowLayoutPanel body,
        Type type,
        BindingFlags flags,
        AramMetaPage page
    )
    {
        foreach (Control child in body.Controls.Cast<Control>().ToArray())
        {
            child.Dispose();
        }

        body.Controls.Add(new Panel { Size = new Size(282, 84), Margin = new Padding(0, 0, 0, 4) });
        type.GetField("_hero", flags)!.SetValue(assistant, 103);
        type.GetMethod("AddMasterySummary", flags)!.Invoke(assistant, null);
        type.GetMethod("AddMayhemRecommendations", flags)!
            .Invoke(assistant, new object[] { page, 103, CancellationToken.None });
        Application.DoEvents();
        bool ContainsConfigure(Control c) =>
            c is Button b && b.Text == "配置出装" || c.Controls.Cast<Control>().Any(ContainsConfigure);
        if (!ContainsConfigure(body))
        {
            throw new Exception("Mayhem overview exposes the equipment editor action");
        }

        using (var overview = new Bitmap(assistant.Width, assistant.Height))
        {
            assistant.DrawToBitmap(overview, new Rectangle(Point.Empty, overview.Size));
            overview.Save(Path.Combine(AppContext.BaseDirectory, "assistant-mayhem-overview.png"));
        }
        if (body.VerticalScroll.Visible || body.HorizontalScroll.Visible)
        {
            throw new Exception(
                $"Mayhem overview must fit one screen: {body.DisplayRectangle}, viewport {body.ClientSize}"
            );
        }
    }
}
