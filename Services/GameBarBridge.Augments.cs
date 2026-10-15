using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed partial class GameBarBridge
{
    /// <summary>The stacking augments the widget's player added for this session; null when missing or malformed.</summary>
    public async Task<AugmentSelection[]?> ReadAugmentSelectionsAsync()
    {
        string session = _detailScope.Session;
        string directory = _testDirectory ?? _directory;
        if (_disposed || session.Length == 0 || directory.Length == 0)
        {
            return null;
        }

        string path = Path.Combine(directory, "augment-stacks.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096)
            {
                return null;
            }

            return AugmentSelections.Parse(
                JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)),
                session
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            // The widget replaces the file atomically; a read during the swap is retried on the next poll.
            return null;
        }
    }

    /// <summary>Ultimate haste entered on the augment page for this session; null when missing or malformed.</summary>
    public async Task<Dictionary<string, double>?> ReadUltimateHasteAsync()
    {
        string session = _detailScope.Session;
        string directory = _testDirectory ?? _directory;
        if (_disposed || session.Length == 0 || directory.Length == 0)
        {
            return null;
        }

        string path = Path.Combine(directory, "ultimate-haste.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 8192)
            {
                return null;
            }

            return CooldownOptions.ParseUltimateHaste(
                JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)),
                session
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            // The widget replaces the file atomically; a read during the swap is retried on the next poll.
            return null;
        }
    }

    /// <summary>The augments the augment page offers, so the widget's menu follows the main program's list.</summary>
    private static JArray AugmentCatalogJson() =>
        new(
            AugmentCatalog.All.Select(info => new JObject
            {
                ["augment"] = info.Augment.ToString(),
                ["name"] = info.Name,
                ["kind"] = info.Kind.ToString(),
                ["effect"] = info.Effect,
                ["pinyin"] = AugmentCatalog.Pinyin(info.Augment),
                ["tags"] = new JArray(AugmentCatalog.Tags(info.Augment)),
                ["choices"] = new JArray(
                    info.Choices.Select(choice => new JObject { ["key"] = choice.Key, ["label"] = choice.Label })
                ),
            })
        );

    // The anchor goes back to the widget, which starts its corrections from it and keeps it across its restarts.
    private static JArray AugmentJson(IEnumerable<LiveAugmentStack> stacks) =>
        new(
            stacks.Select(stack => new JObject
            {
                ["player"] = stack.Selection.Player,
                ["augment"] = stack.Selection.Augment.ToString(),
                ["name"] = AugmentCatalog.Get(stack.Selection.Augment).Name,
                ["kind"] = AugmentCatalog.Get(stack.Selection.Augment).Kind.ToString(),
                ["choice"] = AugmentCatalog.Choice(stack.Selection.Augment, stack.Selection.Choice),
                ["time"] = stack.Selection.Anchor.Time,
                ["start"] = stack.Selection.Anchor.Stacks,
                ["stacks"] = stack.State.Stacks,
                ["bonus"] = AugmentEffects.Bonus(
                    stack.Selection.Augment,
                    stack.State.Stacks,
                    AugmentCatalog.Choice(stack.Selection.Augment, stack.Selection.Choice)
                ),
                ["approximate"] = stack.State.Deaths > 0 && stack.Selection.Augment != TrackedAugment.InfiniteRecursion,
                ["complete"] = stack.State.Complete,
                ["note"] =
                    AugmentCatalog.Get(stack.Selection.Augment).Kind == AugmentKind.Takedowns
                        ? $"起点 {stack.Selection.Anchor.Stacks} 层，之后参与击杀 {stack.State.Takedowns} 次、死亡 {stack.State.Deaths} 次。"
                            + stack.State.Note
                        : AugmentCatalog.Get(stack.Selection.Augment).Effect + "。" + stack.State.Note,
            })
        );
}
