// Included inside WidgetView. The stat page for the open hero, at the top of the skills tab: the main program sends the
// stat table (current, base at the level, growth and bonus; 2999's panel for the player, estimates for others), the
// character facts and each ability's tooltip with its numbers already computed for every rank. The page picks the rank
// the cooldown rows set, and records this player's augments in place, as the augment page does for everyone.
static constexpr uint32_t StatsAccent = 0x65d6d6;
hstring statsSignature;

JsonObject SheetData()
{
    if (selectedCard < 0 || static_cast<uint32_t>(selectedCard) >= controlRows.Size())
        return nullptr;
    auto value = controlRows.GetObjectAt(selectedCard).TryLookup(L"sheet");
    return value && value.ValueType() == JsonValueType::Object ? value.GetObject() : nullptr;
}
bool HasStatsPage()
{
    return CalcData() != nullptr || SheetData() != nullptr;
}
static JsonArray StatsArray(JsonObject const &value, wchar_t const *name)
{
    auto entry = value ? value.TryLookup(name) : nullptr;
    return entry && entry.ValueType() == JsonValueType::Array ? entry.GetArray() : JsonArray();
}
static uint32_t StatsStyleColor(hstring const &style)
{
    if (style == L"magic")
        return 0x7fa8ff;
    if (style == L"physical")
        return 0xff9d5c;
    if (style == L"true")
        return 0xf2f2f2;
    if (style == L"heal")
        return 0x6fd67f;
    if (style == L"shield")
        return 0xc6d3dc;
    if (style == L"speed")
        return 0x9fe3c4;
    if (style == L"key")
        return 0xe0c688;
    return 0xc0d5df;
}
// The calculator's entry for an ability and its index among the what-if ranks; -1 when it has none.
std::pair<JsonObject, int> StatsCalcAbility(hstring const &key)
{
    auto abilities = StatsArray(CalcData(), L"abilities");
    for (uint32_t i = 0; i < abilities.Size() && i < calcRanks.size(); ++i)
        if (AugmentText(abilities.GetObjectAt(i), L"key") == key)
            return {abilities.GetObjectAt(i), static_cast<int>(i)};
    return {nullptr, -1};
}
// The rank the card shows for this ability: the user's what-if, else the snapshot's; 0 when unknown.
int StatsRank(hstring const &key)
{
    auto [ability, index] = StatsCalcAbility(key);
    return index >= 0 ? CalcRank(ability, static_cast<size_t>(index)) : 0;
}
// One value per rank, or one for all: the chosen rank's, or every rank joined as the game's tooltip lists them.
static hstring StatsValue(JsonArray const &values, int rank)
{
    if (values.Size() == 0)
        return L"?";
    auto at = [&values](uint32_t index)
    { return values.GetAt(index).ValueType() == JsonValueType::String ? values.GetStringAt(index) : hstring(L"?"); };
    if (values.Size() == 1)
        return at(0);
    if (rank >= 1)
        return at((std::min)(static_cast<uint32_t>(rank - 1), values.Size() - 1));
    std::wstring joined;
    for (uint32_t i = 0; i < values.Size(); ++i)
        joined += (i ? L"/" : L"") + std::wstring(at(i));
    return hstring(joined);
}
Grid StatsTable(JsonArray const &rows)
{
    Grid table;
    for (double width : {118.0, 84.0, 84.0, 120.0, 0.0})
    {
        ColumnDefinition column;
        column.Width(width > 0 ? GridLength{width, GridUnitType::Pixel} : GridLength{1, GridUnitType::Star});
        table.ColumnDefinitions().Append(column);
    }
    auto cell = [&table](hstring const &text, int32_t row, int32_t column, double size, uint32_t color)
    {
        auto value = MakeText(text, size, color);
        value.Margin({0, 2, 6, 2});
        Grid::SetRow(value, row);
        Grid::SetColumn(value, column);
        table.Children().Append(value);
    };
    std::array<hstring, 5> headings{L"属性", L"当前", L"本级基础", L"1 级 · 成长", L"额外"};
    RowDefinition head;
    head.Height({1, GridUnitType::Auto});
    table.RowDefinitions().Append(head);
    for (int32_t i = 0; i < 5; ++i)
        cell(headings[i], 0, i, 11, 0x6f8793);
    for (uint32_t i = 0; i < rows.Size(); ++i)
    {
        auto row = rows.GetObjectAt(i);
        RowDefinition definition;
        definition.Height({1, GridUnitType::Auto});
        table.RowDefinitions().Append(definition);
        int32_t at = static_cast<int32_t>(i + 1);
        table.Children().Append(StatsName(row, at));
        cell(AugmentText(row, L"value"), at, 1, 14, 0xe2eef3);
        cell(AugmentText(row, L"base"), at, 2, 12, 0xa4bcc7);
        cell(AugmentText(row, L"growth"), at, 3, 12, 0xa4bcc7);
        cell(AugmentText(row, L"bonus"), at, 4, 12, 0x65d6d6);
    }
    return table;
}
// The game's own icon for a stat; the icons come with the detail, so the first snapshots may show none.
Image StatsIcon(hstring const &key, double size)
{
    Image icon;
    icon.Width(size);
    icon.Height(size);
    icon.VerticalAlignment(VerticalAlignment::Center);
    if (!key.empty())
        CachedDetailImage(icon, detailData.GetNamedObject(L"statIcons", JsonObject()).GetNamedString(key, L""), 20);
    return icon;
}
StackPanel StatsName(JsonObject const &row, int32_t at)
{
    StackPanel name;
    name.Orientation(Orientation::Horizontal);
    name.Margin({0, 2, 6, 2});
    auto icon = StatsIcon(AugmentText(row, L"icon"), 18);
    icon.Margin({0, 0, 5, 0});
    name.Children().Append(icon);
    auto text = MakeText(AugmentText(row, L"name"), 12, 0x91acbb);
    text.VerticalAlignment(VerticalAlignment::Center);
    name.Children().Append(text);
    Grid::SetRow(name, at);
    return name;
}
// The game's stats panel shows these eight first; health and the resource go in the header line.
static constexpr std::array<wchar_t const *, 8> StatsMainKeys{
    L"ad", L"ap", L"armor", L"mr", L"attackSpeed", L"haste", L"crit", L"moveSpeed"};
static bool StatsIsMain(hstring const &key)
{
    return std::find(StatsMainKeys.begin(), StatsMainKeys.end(), std::wstring_view(key)) != StatsMainKeys.end();
}
static bool StatsIsHeader(JsonObject const &row)
{
    return !AugmentText(row, L"bar").empty();
}
// A value that reads as nothing ("0", "0%", "0/5秒"): shown dim among the main eight and left out of the rest.
static bool StatsIsZero(hstring const &value)
{
    wchar_t *end = nullptr;
    double number = std::wcstod(value.c_str(), &end);
    return !value.empty() && end != value.c_str() && number == 0;
}
// Base at the level, level 1 with growth and the bonus, as the hover of a stat.
static hstring StatsBreakdown(JsonObject const &row)
{
    std::wstring text(AugmentText(row, L"name") + L" " + AugmentText(row, L"value"));
    auto add = [&text, &row](wchar_t const *label, wchar_t const *field)
    {
        auto value = AugmentText(row, field);
        if (!value.empty())
            text += L"\n" + std::wstring(label) + L" " + std::wstring(value);
    };
    add(L"本级基础", L"base");
    add(L"1 级 · 成长", L"growth");
    add(L"额外", L"bonus");
    return hstring(text);
}
// Icon, value and name; the main eight larger, on a tile.
FrameworkElement StatsTile(JsonObject const &row, bool main)
{
    bool zero = StatsIsZero(AugmentText(row, L"value"));
    StackPanel tile;
    tile.Orientation(Orientation::Horizontal);
    auto icon = StatsIcon(AugmentText(row, L"icon"), main ? 22 : 16);
    icon.Margin({0, 0, main ? 8.0 : 5.0, 0});
    icon.Opacity(zero ? 0.4 : 1);
    tile.Children().Append(icon);
    auto value = MakeText(AugmentText(row, L"value"), main ? 16 : 12, zero ? 0x5f7884 : 0xe2eef3);
    value.VerticalAlignment(VerticalAlignment::Center);
    tile.Children().Append(value);
    auto bonus = AugmentText(row, L"bonus");
    if (main && !bonus.empty() && bonus.c_str()[0] == L'+')
    {
        auto extra = MakeText(bonus, 11, 0x65d6d6);
        extra.Margin({4, 0, 0, 0});
        extra.VerticalAlignment(VerticalAlignment::Center);
        tile.Children().Append(extra);
    }
    auto name = MakeText(AugmentText(row, L"name"), 11, zero ? 0x4e6470 : 0x6f8793);
    name.Margin({6, 0, 0, 0});
    name.VerticalAlignment(VerticalAlignment::Center);
    name.TextTrimming(TextTrimming::CharacterEllipsis);
    tile.Children().Append(name);
    if (!main)
    {
        tile.Margin({0, 1, 8, 1});
        SetRecordTip(tile, StatsBreakdown(row));
        return tile;
    }
    Border frame;
    frame.Background(MakeBrush(0x143542));
    frame.CornerRadius({4, 4, 4, 4});
    frame.Padding({8, 5, 8, 5});
    frame.Margin({0, 0, 6, 6});
    frame.Child(tile);
    SetRecordTip(frame, StatsBreakdown(row));
    return frame;
}
// The level as the game's badge beside the portrait.
static Border StatsLevelBadge(int level)
{
    Border badge;
    badge.Width(28);
    badge.Height(28);
    badge.CornerRadius({14, 14, 14, 14});
    badge.Background(MakeBrush(0x0e2630));
    badge.BorderBrush(MakeBrush(0xc8aa6e));
    badge.BorderThickness({1.5});
    auto text = MakeText(to_hstring(level), 13, 0xe9d8a6);
    text.HorizontalAlignment(HorizontalAlignment::Center);
    text.VerticalAlignment(VerticalAlignment::Center);
    badge.Child(text);
    return badge;
}
// Health or the resource as the HUD's bar, in its colour. 2999 gives the maximum only, so the bar is full: the colour
// says which it is, not how much is left. Mana needs no name; the others say what the resource is.
static Border StatsBar(JsonObject const &row)
{
    auto bar = AugmentText(row, L"bar");
    uint32_t color = bar == L"health" ? 0x2f8a3e : bar == L"mana" ? 0x2f6fb5 : bar == L"energy" ? 0xb08d1e : 0xa83434;
    bool named = bar != L"health" && bar != L"mana";
    Border frame;
    frame.MinWidth(130);
    frame.Height(20);
    frame.CornerRadius({3, 3, 3, 3});
    frame.Background(MakeBrush(color));
    frame.Margin({10, 0, 0, 0});
    frame.Padding({8, 0, 8, 0});
    frame.VerticalAlignment(VerticalAlignment::Center);
    auto text = MakeText((named ? AugmentText(row, L"name") + L" " : hstring()) + AugmentText(row, L"value"), 12);
    text.HorizontalAlignment(HorizontalAlignment::Center);
    text.VerticalAlignment(VerticalAlignment::Center);
    frame.Child(text);
    SetRecordTip(frame, StatsBreakdown(row));
    return frame;
}
// The level, health and the resource as the game shows them, and a mark only when the stats are an estimate: the
// player's own come from 2999 and need no note.
Grid StatsHeader(JsonObject const &sheet)
{
    Grid header;
    header.Margin({0, 0, 0, 8});
    ColumnDefinition left, right;
    left.Width({1, GridUnitType::Star});
    right.Width({1, GridUnitType::Auto});
    header.ColumnDefinitions().Append(left);
    header.ColumnDefinitions().Append(right);
    StackPanel bars;
    bars.Orientation(Orientation::Horizontal);
    bars.Children().Append(StatsLevelBadge(static_cast<int>(sheet.GetNamedNumber(L"level", 1))));
    for (auto const &entry : StatsArray(sheet, L"stats"))
        if (StatsIsHeader(entry.GetObject()))
            bars.Children().Append(StatsBar(entry.GetObject()));
    header.Children().Append(bars);
    if (!sheet.GetNamedBoolean(L"estimated", true))
        return header;
    Border mark;
    mark.Background(MakeBrush(0x3d3220));
    mark.BorderBrush(MakeBrush(0xe0b25c));
    mark.BorderThickness({1});
    mark.CornerRadius({3, 3, 3, 3});
    mark.Padding({8, 1, 8, 1});
    mark.VerticalAlignment(VerticalAlignment::Center);
    mark.Child(MakeText(L"估算 ⚠", 12, 0xe0b25c));
    SetRecordTip(mark, AugmentText(sheet, L"source"));
    Grid::SetColumn(mark, 1);
    header.Children().Append(mark);
    return header;
}
// The stats as the game's panel shows them: the main eight as tiles, the rest in small type below.
Border StatsPanel(JsonObject const &sheet)
{
    StackPanel content;
    content.Children().Append(StatsHeader(sheet));
    std::vector<FrameworkElement> main, other;
    for (auto const &entry : StatsArray(sheet, L"stats"))
    {
        auto row = entry.GetObject();
        if (StatsIsMain(AugmentText(row, L"icon")))
            main.push_back(StatsTile(row, true));
        else if (!StatsIsHeader(row) && !StatsIsZero(AugmentText(row, L"value")))
            other.push_back(StatsTile(row, false));
    }
    if (!main.empty())
        content.Children().Append(RecordGrid(main, 4));
    if (!other.empty())
        content.Children().Append(RecordGrid(other, 4));
    return RecordCard(content, StatsAccent);
}
// The full table and the character facts, folded: reference, rarely needed during a game.
bool statsMoreOpen = false;
void RenderStatsMore(JsonObject const &sheet)
{
    auto toggle = DetailButton(statsMoreOpen ? L"更多数据 ▴" : L"更多数据 ▾");
    toggle.Padding({10, 3, 10, 3});
    toggle.Margin({0, 2, 0, 6});
    toggle.Click(
        [this](auto &&, auto &&)
        {
            statsMoreOpen = !statsMoreOpen;
            RenderDetails(false);
        });
    detailBody.Children().Append(toggle);
    if (!statsMoreOpen)
        return;
    StackPanel content;
    auto rows = StatsArray(sheet, L"stats");
    if (rows.Size() > 0)
        content.Children().Append(StatsTable(rows));
    std::vector<FrameworkElement> extras;
    for (auto const &entry : StatsArray(sheet, L"extras"))
    {
        auto extra = entry.GetObject();
        auto chip = ControlChip(
            AugmentText(extra, L"name") + L" " + AugmentText(extra, L"value"), 0xa4bcc7, AugmentText(extra, L"note"));
        extras.push_back(chip.as<FrameworkElement>());
    }
    if (!extras.empty())
    {
        auto grid = RecordGrid(extras, 3);
        grid.Margin({0, 6, 0, 0});
        content.Children().Append(grid);
    }
    detailBody.Children().Append(RecordCard(content, StatsAccent));
}
// The tooltip as runs: text in the tooltip's own colors, numbers at the rank the cooldown rows show.
TextBlock StatsTooltip(JsonArray const &segments, int rank)
{
    TextBlock text;
    text.TextWrapping(TextWrapping::Wrap);
    text.FontSize(13);
    text.Foreground(MakeBrush(0xc0d5df));
    for (auto const &entry : segments)
    {
        if (entry.ValueType() != JsonValueType::Object)
            continue;
        auto segment = entry.GetObject();
        auto style = AugmentText(segment, L"style");
        Windows::UI::Xaml::Documents::Run run;
        auto values = StatsArray(segment, L"values");
        bool unknown = segment.GetNamedBoolean(L"unknown", false);
        if (values.Size() > 0)
        {
            run.Text(StatsValue(values, rank) + (segment.GetNamedBoolean(L"partial", false) ? L"+" : L""));
            run.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        }
        else
            run.Text(AugmentText(segment, L"text"));
        run.Foreground(
            MakeBrush(unknown ? 0x6f8793 : StatsStyleColor(style.empty() && values.Size() > 0 ? L"key" : style)));
        text.Inlines().Append(run);
    }
    return text;
}
// The right of a card's title: the rank, with steps when the calculator knows the ability, the cost and the cooldown
// with this page's haste, as the game's tooltip puts them beside the name.
StackPanel StatsSpellFacts(JsonObject const &spell, int rank)
{
    auto key = AugmentText(spell, L"key");
    int ranks = static_cast<int>(spell.GetNamedNumber(L"ranks", 1));
    auto [ability, index] = StatsCalcAbility(key);
    StackPanel facts;
    facts.Orientation(Orientation::Horizontal);
    facts.VerticalAlignment(VerticalAlignment::Center);
    auto add = [&facts](hstring const &text, double size, uint32_t color)
    {
        auto note = MakeText(text, size, color);
        note.Margin({10, 0, 0, 0});
        note.VerticalAlignment(VerticalAlignment::Center);
        facts.Children().Append(note);
        return note;
    };
    if (index >= 0)
    {
        size_t slot = static_cast<size_t>(index);
        facts.Children().Append(CalcButton(L"−", [this, slot, rank] { calcRanks[slot] = rank - 1; }, rank > 0));
        auto level = MakeText(rank > 0 ? to_hstring(rank) + L"/" + to_hstring(ranks) + L" 级" : hstring(L"未学"),
                              12,
                              rank > 0 ? 0xe2eef3 : 0x6f8793);
        level.Margin({6, 0, 2, 0});
        level.VerticalAlignment(VerticalAlignment::Center);
        facts.Children().Append(level);
        facts.Children().Append(CalcButton(L"+", [this, slot, rank] { calcRanks[slot] = rank + 1; }, rank < ranks));
    }
    else if (key != L"P")
        add(rank > 0 ? to_hstring(rank) + L"/" + to_hstring(ranks) + L" 级" : hstring(L"未学 · 显示各级"),
            12,
            0x91acbb);
    auto costs = StatsArray(spell, L"costs");
    if (costs.Size() > 0)
        add(L"消耗 " + StatsValue(costs, rank), 12, 0x91acbb);
    if (index >= 0 && rank > 0)
    {
        auto calc = CalcData();
        double haste = calcHaste >= 0 ? calcHaste : calc.GetNamedNumber(L"haste", 0);
        auto [baseText, resultText] = CalcCooldownText(
            ability, rank, haste, calc.GetNamedBoolean(L"hasteEstimated", true), CalcOptionsFrom(calc));
        auto cooldown = add(L"冷却 " + resultText, 13, 0x65d6d6);
        SetRecordTip(cooldown, baseText);
    }
    return facts;
}
// One ability as the game's tooltip shows it: icon, name, rank, cost and cooldown, then the description with its
// numbers at that rank.
Border StatsSpellCard(JsonObject const &spell)
{
    auto key = AugmentText(spell, L"key");
    int rank = key == L"P" ? 1 : StatsRank(key);
    auto listed = SkillIcon(key);
    StackPanel content;
    Grid header;
    for (auto width : {GridUnitType::Auto, GridUnitType::Star, GridUnitType::Auto})
    {
        ColumnDefinition column;
        column.Width({1, width});
        header.ColumnDefinitions().Append(column);
    }
    Image icon;
    icon.Width(32);
    icon.Height(32);
    icon.Margin({0, 0, 8, 0});
    if (listed)
        CachedDetailImage(icon, listed.GetNamedString(L"image", L""), 48);
    header.Children().Append(icon);
    auto title = MakeText((key == L"P" ? hstring(L"被动") : key) + L"  " + AugmentText(spell, L"name"), 14, 0xe0c688);
    title.VerticalAlignment(VerticalAlignment::Center);
    title.TextTrimming(TextTrimming::CharacterEllipsis);
    Grid::SetColumn(title, 1);
    header.Children().Append(title);
    auto facts = StatsSpellFacts(spell, rank);
    Grid::SetColumn(facts, 2);
    header.Children().Append(facts);
    content.Children().Append(header);
    auto segments = StatsArray(spell, L"segments");
    // While the game's tooltips load, the client's description stands in.
    auto plain = listed ? listed.GetNamedString(L"description", L"") : hstring();
    if (segments.Size() == 0)
    {
        auto text =
            MakeText(plain.empty() ? hstring(L"暂无技能说明。") : plain, 13, plain.empty() ? 0x6f8793 : 0xc0d5df);
        text.TextWrapping(TextWrapping::Wrap);
        text.Margin({0, 4, 0, 0});
        content.Children().Append(text);
    }
    else
    {
        auto text = StatsTooltip(segments, rank);
        text.Margin({0, 4, 0, 0});
        content.Children().Append(text);
    }
    return RecordCard(content, 0xe0c688);
}
// This player's augments: the only place they are recorded, for any of the ten players.
void RenderStatsAugments()
{
    if (!supportsAugments || selectedCard < 0 || static_cast<uint32_t>(selectedCard) >= controlRows.Size())
        return;
    auto row = controlRows.GetObjectAt(selectedCard);
    if (row.GetNamedNumber(L"championId", 0) <= 0)
        return;
    bool timed = WaveGameTime() >= 0;
    if (!timed)
    {
        auto wait = MakeText(L"等待对局时间：自动计层的海克斯（参与击杀叠层）暂时不能添加或修正。", 12, 0xe0b25c);
        wait.TextWrapping(TextWrapping::Wrap);
        wait.Margin({0, 0, 0, 4});
        detailBody.Children().Append(wait);
    }
    detailBody.Children().Append(
        CreateAugmentLine(static_cast<uint32_t>(selectedCard), selectedCard < 5 ? 0x45c7ce : 0xf3b37e, timed));
}
// How the cards' numbers are made: where the descriptions come from, what "?" and "+" mean, and the cooldown rules.
TextBlock StatsNumbersNote(hstring const &cooldowns)
{
    auto sheet = SheetData();
    auto note = MakeText(L"技能数值说明 ⓘ", 12, 0x91acbb);
    note.Margin({10, 0, 0, 0});
    note.VerticalAlignment(VerticalAlignment::Center);
    auto texts = sheet ? AugmentText(sheet, L"textSource") : hstring();
    SetRecordTip(note, texts + (texts.empty() || cooldowns.empty() ? L"" : L"\n\n") + cooldowns);
    return note;
}
// One line above the ability cards: the haste their cooldowns use, adjustable, with the rules on hover; then what the
// recorded augments change, when they change anything.
void RenderStatsHaste(JsonObject const &calc)
{
    auto options = CalcOptionsFrom(calc);
    double snapshotHaste = calc.GetNamedNumber(L"haste", 0);
    double haste = calcHaste >= 0 ? calcHaste : snapshotHaste;
    auto row = CalcHasteRow(haste, snapshotHaste, calc.GetNamedString(L"hasteDetails", L""));
    bool ranksChanged = std::any_of(calcRanks.begin(), calcRanks.end(), [](int rank) { return rank >= 0; });
    row.Children().Append(CalcButton(L"恢复技能等级", [this] { calcRanks.fill(-1); }, ranksChanged));
    row.Children().Append(StatsNumbersNote(CalcGuide(calc)));
    detailBody.Children().Append(row);
    if (!options.summary.empty())
        detailBody.Children().Append(CalcRecordedRow(options));
}
// Top to bottom: the stats as the game's panel shows them, this player's augments, the haste, then each ability with
// its rank, cost, cooldown and numbers. Without the ability details the calculator stands alone.
void RenderStatsPage()
{
    auto sheet = SheetData();
    auto calc = CalcData();
    if (!sheet && !calc)
    {
        RenderMissingSection();
        return;
    }
    statsSignature = StatsPageSignature();
    if (calc)
        ResetCalculatorFor(selectedCard);
    if (!sheet)
    {
        RenderStatsAugments();
        RenderCooldownCalculator();
        return;
    }
    detailBody.Children().Append(StatsPanel(sheet));
    RenderStatsAugments();
    if (calc)
        RenderStatsHaste(calc);
    else
    {
        auto note = StatsNumbersNote(hstring());
        note.Margin({0, 0, 0, 4});
        detailBody.Children().Append(note);
    }
    for (auto const &entry : StatsArray(sheet, L"spells"))
        if (entry.ValueType() == JsonValueType::Object)
            detailBody.Children().Append(StatsSpellCard(entry.GetObject()));
}
// Everything the page shows that a snapshot can change: the stats and tooltips, the cooldowns and the augments.
hstring StatsPageSignature()
{
    auto sheet = SheetData();
    auto calc = CalcData();
    return (sheet ? sheet.Stringify() : hstring()) + L"|" + (calc ? calc.Stringify() : hstring()) + L"|" +
           AugmentRecordSignature();
}
bool StatsPageShown()
{
    return selectedCard >= 0 && detailTab == L"skills" && HasStatsPage() &&
           detailRoot.Visibility() == Visibility::Visible;
}
// The user's own augment changes show at once; the add list closes itself first.
void RenderStatsAfterEdit()
{
    if (StatsPageShown())
        RenderDetails(false);
}
// Snapshots redraw the page only when it changed, and never while the add list is open: a redraw removes the
// button the list hangs from and closes it mid-typing.
void RefreshStatsPage()
{
    if (!StatsPageShown() || StatsPageSignature() == statsSignature)
        return;
    if (augmentPickerOpen)
    {
        augmentRedrawPending = true;
        return;
    }
    RefreshDetailHeader();
    RenderDetails(false);
}
