// Included inside WidgetView. The cooldown calculator for the open hero: the main program sends each ability's
// cooldowns by rank, the ranks (2999's for the player, the highest the level allows for others), the ability haste
// and the cooldown rules of the player's recorded augments. Ranks and haste are what-ifs the user may change here;
// the stat page around it records the augments and ultimate haste, which this part only shows.
static constexpr double BreadHaste = 100;
static constexpr double PuristCooldown = 0.9;
// Ult Bot (Wiki): ability haste no longer shortens basic abilities; it grants 100 ability haste, and the ultimate's
// cooldown uses (ability haste + ultimate haste) × 1.5. The stats panel, and so 2999, already shows the 100.
static constexpr double UltBotHaste = 100;
static constexpr double UltBotEffect = 1.5;
int calcCard = -1, calcHero = 0;
double calcHaste = -1;
std::array<int, 4> calcRanks{-1, -1, -1, -1};
hstring calcSignature;
struct CalcOptions
{
    std::array<bool, 3> bread{};
    bool purist = false;
    int pursuit = -1;
    double pursuitHaste = 80;
    double ultHaste = 0;
    bool ultBot = false;
    bool ultimateSealed = false;
    hstring summary;
};

JsonObject CalcData()
{
    if (selectedCard < 0 || static_cast<uint32_t>(selectedCard) >= controlRows.Size())
        return nullptr;
    auto value = controlRows.GetObjectAt(selectedCard).TryLookup(L"calc");
    return value && value.ValueType() == JsonValueType::Object ? value.GetObject() : nullptr;
}
bool HasCalculator()
{
    return CalcData() != nullptr;
}
// The user's changes belong to one hero; opening another starts from the snapshot again.
void ResetCalculatorFor(int card)
{
    if (calcCard == card && calcHero == cards[card].hero)
        return;
    calcCard = card;
    calcHero = cards[card].hero;
    calcHaste = -1;
    calcRanks.fill(-1);
}
static CalcOptions CalcOptionsFrom(JsonObject const &calc)
{
    CalcOptions options;
    auto value = calc ? calc.TryLookup(L"options") : nullptr;
    if (!value || value.ValueType() != JsonValueType::Object)
        return options;
    auto echo = value.GetObject();
    auto flag = [&echo](wchar_t const *name)
    {
        auto entry = echo.TryLookup(name);
        return entry && entry.ValueType() == JsonValueType::Boolean && entry.GetBoolean();
    };
    auto number = [&echo](wchar_t const *name, double fallback)
    {
        auto entry = echo.TryLookup(name);
        return entry && entry.ValueType() == JsonValueType::Number ? entry.GetNumber() : fallback;
    };
    auto bread = echo.TryLookup(L"bread");
    if (bread && bread.ValueType() == JsonValueType::Array)
        for (uint32_t i = 0; i < 3 && i < bread.GetArray().Size(); ++i)
            options.bread[i] =
                bread.GetArray().GetAt(i).ValueType() == JsonValueType::Boolean && bread.GetArray().GetBooleanAt(i);
    options.purist = flag(L"purist");
    options.ultBot = flag(L"ultBot");
    options.ultimateSealed = flag(L"ultimateSealed");
    options.summary = AugmentText(echo, L"summary");
    options.pursuitHaste = number(L"pursuitHaste", 80);
    options.ultHaste = (std::max)(0.0, number(L"ultimateHaste", 0));
    auto pursuit = echo.TryLookup(L"pursuit");
    if (pursuit && pursuit.ValueType() == JsonValueType::String)
    {
        auto at = std::wstring_view(L"QWER").find(std::wstring_view(pursuit.GetString()));
        options.pursuit = pursuit.GetString().size() == 1 && at != std::wstring_view::npos ? static_cast<int>(at) : -1;
    }
    return options;
}
// Haste and the level change during the game; the page follows them unless the user set its own values. The stat
// page around the calculator redraws when anything it shows changed.
void ApplyCalculator()
{
    auto calc = CalcData();
    auto signature = calc ? calc.Stringify() : hstring();
    if (signature != calcSignature)
    {
        calcSignature = signature;
        if (selectedCard >= 0)
            RefreshDetailHeader();
    }
    RefreshStatsPage();
}
static hstring CalcSeconds(double seconds)
{
    wchar_t text[32]{};
    swprintf_s(text, seconds < 10 ? L"%.1f 秒" : L"%.0f 秒", seconds);
    return text;
}
static double CalcNumber(JsonArray const &values, uint32_t index)
{
    return index < values.Size() && values.GetAt(index).ValueType() == JsonValueType::Number ? values.GetNumberAt(index)
                                                                                             : 0;
}
Button CalcButton(hstring const &text, std::function<void()> action, bool enabled = true)
{
    auto button = DetailButton(text);
    button.Padding({8, 2, 8, 2});
    button.Margin({4, 0, 0, 0});
    button.IsEnabled(enabled);
    button.Click(
        [this, action](auto &&, auto &&)
        {
            action();
            RenderDetails(false);
        });
    return button;
}
StackPanel CalcHasteRow(double haste, double snapshotHaste, hstring const &details)
{
    StackPanel row;
    row.Orientation(Orientation::Horizontal);
    row.Margin({0, 4, 0, 4});
    auto label = MakeText(L"技能急速", 13, 0x91acbb);
    label.Width(72);
    label.VerticalAlignment(VerticalAlignment::Center);
    row.Children().Append(label);
    auto value = MakeText(to_hstring(static_cast<int>(std::lround(haste))), 16, 0xe2eef3);
    value.Width(48);
    value.VerticalAlignment(VerticalAlignment::Center);
    SetRecordTip(value, details);
    row.Children().Append(value);
    for (int step : {-10, -1, 1, 10})
    {
        hstring text = step > 0 ? L"+" + to_hstring(step) : L"−" + to_hstring(-step);
        row.Children().Append(CalcButton(text, [this, haste, step] { calcHaste = (std::max)(0.0, haste + step); }));
    }
    row.Children().Append(
        CalcButton(L"恢复", [this] { calcHaste = -1; }, calcHaste >= 0 && calcHaste != snapshotHaste));
    return row;
}
// The haste one ability's cooldown uses: general haste (all but the ultimate's under Ult Bot), ultimate haste for
// R, and the haste augments give to that ability alone; the same rules as the main program's CooldownOptions.
static double CalcAbilityHaste(CalcOptions const &options, size_t slot, double haste, bool estimated)
{
    double general = haste + (options.ultBot && estimated ? UltBotHaste : 0);
    double own = (slot < options.bread.size() && options.bread[slot] ? BreadHaste : 0) +
                 (static_cast<int>(slot) == options.pursuit ? options.pursuitHaste : 0);
    if (slot == 3)
        return (general + options.ultHaste) * (options.ultBot ? UltBotEffect : 1) + own;
    return (options.ultBot ? 0 : general) + own;
}
// What the recorded augments change here, read-only; they are recorded in the augment line above.
StackPanel CalcRecordedRow(CalcOptions const &options)
{
    StackPanel row;
    row.Orientation(Orientation::Horizontal);
    row.Margin({0, 0, 0, 6});
    auto label = MakeText(L"海克斯", 13, 0x91acbb);
    label.Width(72);
    label.VerticalAlignment(VerticalAlignment::Center);
    row.Children().Append(label);
    auto text = MakeText(options.summary.empty() ? hstring(L"未记录影响冷却的海克斯") : options.summary,
                         13,
                         options.summary.empty() ? 0x6f8793 : 0xb8a2ff);
    text.VerticalAlignment(VerticalAlignment::Center);
    text.TextWrapping(TextWrapping::Wrap);
    text.MaxWidth(520);
    row.Children().Append(text);
    return row;
}
// The base cooldown at the rank and the cooldown with haste and the recorded augments; "未学" before the first rank.
static std::pair<hstring, hstring> CalcCooldownText(
    JsonObject const &ability, int rank, double haste, bool estimated, CalcOptions const &options)
{
    auto key = ability.GetNamedString(L"key", L"");
    size_t slot = std::wstring_view(L"QWER").find(std::wstring_view(key));
    auto cooldowns = ability.GetNamedArray(L"cooldowns", JsonArray());
    auto recharge = ability.GetNamedArray(L"recharge", JsonArray());
    auto charges = ability.GetNamedArray(L"charges", JsonArray());
    hstring baseText, resultText;
    if (options.ultimateSealed && slot == 3)
    {
        baseText = L"回归基本功";
        resultText = L"已封印";
    }
    else if (rank == 0)
        resultText = L"未学";
    else
    {
        uint32_t at = static_cast<uint32_t>(rank - 1);
        bool charged = CalcNumber(recharge, at) > 0;
        double seconds = charged ? CalcNumber(recharge, at) : CalcNumber(cooldowns, at);
        double abilityHaste = key.size() == 1 && slot != std::wstring_view::npos
                                  ? CalcAbilityHaste(options, slot, haste, estimated)
                                  : haste;
        double result = seconds * 100 / (100 + (std::max)(0.0, abilityHaste)) * (options.purist ? PuristCooldown : 1);
        int count = static_cast<int>(CalcNumber(charges, at));
        baseText = seconds > 0 ? L"基础 " + CalcSeconds(seconds) : hstring(L"数据里没有冷却");
        resultText = seconds <= 0 ? hstring(L"—")
                     : charged    ? L"充能 " + CalcSeconds(result) + L" · " + to_hstring(count) + L" 层"
                                  : CalcSeconds(result);
    }
    return {baseText, resultText};
}
// The rank the calculator uses for one ability: the user's what-if, else the snapshot's.
int CalcRank(JsonObject const &ability, size_t index)
{
    int ranks = static_cast<int>(ability.GetNamedArray(L"cooldowns", JsonArray()).Size());
    int rank = calcRanks[index] >= 0 ? calcRanks[index] : static_cast<int>(ability.GetNamedNumber(L"rank", 0));
    return (std::clamp)(rank, 0, ranks);
}
// One line per ability: rank with steps, the base cooldown at that rank and the result.
Grid CalcAbilityRow(JsonObject const &ability, size_t index, double haste, bool estimated, CalcOptions const &options)
{
    int ranks = static_cast<int>(ability.GetNamedArray(L"cooldowns", JsonArray()).Size());
    int rank = CalcRank(ability, index);
    Grid row;
    row.Margin({0, 3, 0, 3});
    for (double width : {120.0, 150.0, 130.0, 0.0})
    {
        ColumnDefinition column;
        column.Width(width > 0 ? GridLength{width, GridUnitType::Pixel} : GridLength{1, GridUnitType::Star});
        row.ColumnDefinitions().Append(column);
    }
    auto name =
        MakeText(ability.GetNamedString(L"key", L"") + L" " + ability.GetNamedString(L"name", L""), 13, 0xe0c688);
    name.VerticalAlignment(VerticalAlignment::Center);
    name.TextTrimming(TextTrimming::CharacterEllipsis);
    row.Children().Append(name);
    StackPanel steps;
    steps.Orientation(Orientation::Horizontal);
    steps.Children().Append(CalcButton(L"−", [this, index, rank] { calcRanks[index] = rank - 1; }, rank > 0));
    auto level = MakeText(to_hstring(rank) + L"/" + to_hstring(ranks) + L" 级", 13, 0xe2eef3);
    level.Margin({6, 0, 2, 0});
    level.VerticalAlignment(VerticalAlignment::Center);
    steps.Children().Append(level);
    steps.Children().Append(CalcButton(L"+", [this, index, rank] { calcRanks[index] = rank + 1; }, rank < ranks));
    Grid::SetColumn(steps, 1);
    row.Children().Append(steps);
    auto [baseText, resultText] = CalcCooldownText(ability, rank, haste, estimated, options);
    auto base = MakeText(baseText, 12, 0x91acbb);
    base.VerticalAlignment(VerticalAlignment::Center);
    Grid::SetColumn(base, 2);
    row.Children().Append(base);
    auto value = MakeText(resultText, 15, rank == 0 ? 0x6f8793 : 0x65d6d6);
    value.VerticalAlignment(VerticalAlignment::Center);
    Grid::SetColumn(value, 3);
    row.Children().Append(value);
    return row;
}
// Where the ranks and haste come from and how the cooldown is computed.
static hstring CalcGuide(JsonObject const &calc)
{
    return calc.GetNamedString(L"rankSource", L"") + L" " + calc.GetNamedString(L"hasteDetails", L"") +
           L"\n冷却 = 基础冷却 × 100 / (100 + 技能急速)，大招再加大招急速；已记录的海克斯按各自规则计入"
           L"（面包系列、急速之追求只加对应技能，纯粹主义者 - 术师再乘 0.9，大招工具人只作用于大招并按 1.5 倍）。"
           L"充能技能算每层充能时间。命中返还未计入。";
}
// Without the stat page's ability details the calculator stands alone, its ability rows included.
void RenderCooldownCalculator()
{
    auto calc = CalcData();
    if (!calc)
    {
        RenderMissingSection();
        return;
    }
    ResetCalculatorFor(selectedCard);
    auto options = CalcOptionsFrom(calc);
    double snapshotHaste = calc.GetNamedNumber(L"haste", 0);
    double haste = calcHaste >= 0 ? calcHaste : snapshotHaste;
    StackPanel content;
    auto heading = MakeText(L"冷却计算", 15, 0xe0c688);
    content.Children().Append(heading);
    auto guide = MakeText(CalcGuide(calc), 12, 0x91acbb);
    guide.TextWrapping(TextWrapping::Wrap);
    guide.Margin({0, 2, 0, 4});
    content.Children().Append(guide);
    content.Children().Append(CalcHasteRow(haste, snapshotHaste, calc.GetNamedString(L"hasteDetails", L"")));
    content.Children().Append(CalcRecordedRow(options));
    bool estimated = calc.GetNamedBoolean(L"hasteEstimated", true);
    auto abilities = calc.GetNamedArray(L"abilities", JsonArray());
    for (uint32_t i = 0; i < abilities.Size() && i < calcRanks.size(); ++i)
        content.Children().Append(CalcAbilityRow(abilities.GetObjectAt(i), i, haste, estimated, options));
    auto reset = CalcButton(L"恢复等级和急速",
                            [this]
                            {
                                calcCard = -1;
                                ResetCalculatorFor(selectedCard);
                            });
    reset.HorizontalAlignment(HorizontalAlignment::Left);
    reset.Margin({0, 6, 0, 0});
    content.Children().Append(reset);
    detailBody.Children().Append(RecordCard(content, 0x65d6d6));
}
