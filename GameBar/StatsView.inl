// Included inside WidgetView. The stat page for the open hero: the main program sends the stat table (current, base at
// the level, growth and bonus; 2999's panel for the player, estimates for others), the character facts and each
// ability's tooltip with its numbers already computed for every rank. The page picks the rank the cooldown rows set,
// and records this player's augments in place, as the augment page does for everyone.
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
// The rank the cooldown rows use for this ability: the user's what-if, else the snapshot's; 0 when unknown.
int StatsRank(hstring const &key)
{
    auto abilities = StatsArray(CalcData(), L"abilities");
    for (uint32_t i = 0; i < abilities.Size() && i < calcRanks.size(); ++i)
    {
        auto ability = abilities.GetObjectAt(i);
        if (AugmentText(ability, L"key") != key)
            continue;
        int ranks = static_cast<int>(StatsArray(ability, L"cooldowns").Size());
        int rank = calcRanks[i] >= 0 ? calcRanks[i] : static_cast<int>(ability.GetNamedNumber(L"rank", 0));
        return (std::clamp)(rank, 0, ranks);
    }
    return 0;
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
    for (double width : {96.0, 84.0, 84.0, 120.0, 0.0})
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
        cell(AugmentText(row, L"name"), at, 0, 12, 0x91acbb);
        cell(AugmentText(row, L"value"), at, 1, 14, 0xe2eef3);
        cell(AugmentText(row, L"base"), at, 2, 12, 0xa4bcc7);
        cell(AugmentText(row, L"growth"), at, 3, 12, 0xa4bcc7);
        cell(AugmentText(row, L"bonus"), at, 4, 12, 0x65d6d6);
    }
    return table;
}
Border StatsSheetCard(JsonObject const &sheet)
{
    StackPanel content;
    content.Children().Append(MakeText(L"属性", 15, 0xe0c688));
    auto source = MakeText(AugmentText(sheet, L"source"), 12, 0x91acbb);
    source.TextWrapping(TextWrapping::Wrap);
    source.Margin({0, 2, 0, 4});
    content.Children().Append(source);
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
    return RecordCard(content, StatsAccent);
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
Border StatsSpellCard(JsonObject const &spell)
{
    auto key = AugmentText(spell, L"key");
    int ranks = static_cast<int>(spell.GetNamedNumber(L"ranks", 1));
    int rank = key == L"P" ? 1 : StatsRank(key);
    StackPanel content;
    StackPanel header;
    header.Orientation(Orientation::Horizontal);
    header.Children().Append(
        MakeText((key == L"P" ? hstring(L"被动") : key) + L"  " + AugmentText(spell, L"name"), 14, 0xe0c688));
    hstring level = key == L"P" ? hstring()
                    : rank > 0  ? to_hstring(rank) + L"/" + to_hstring(ranks) + L" 级"
                                : hstring(L"未学 · 显示各级");
    auto costs = StatsArray(spell, L"costs");
    if (costs.Size() > 0)
        level = level + (level.empty() ? L"" : L" · ") + L"消耗 " + StatsValue(costs, rank);
    if (!level.empty())
    {
        auto note = MakeText(level, 12, 0x91acbb);
        note.Margin({10, 2, 0, 0});
        header.Children().Append(note);
    }
    content.Children().Append(header);
    auto segments = StatsArray(spell, L"segments");
    if (segments.Size() == 0)
        content.Children().Append(MakeText(L"暂无技能说明。", 12, 0x6f8793));
    else
    {
        auto text = StatsTooltip(segments, rank);
        text.Margin({0, 4, 0, 0});
        content.Children().Append(text);
    }
    return RecordCard(content, 0xe0c688);
}
// This player's augments, recorded here as on the augment page; the button there still covers all ten players.
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
        CreateAugmentRow(static_cast<uint32_t>(selectedCard), row, selectedCard < 5 ? 0x45c7ce : 0xf3b37e, timed));
}
void RenderStatsPage()
{
    auto sheet = SheetData();
    if (!sheet && !CalcData())
    {
        RenderMissingSection();
        return;
    }
    statsSignature = StatsPageSignature();
    if (sheet)
        detailBody.Children().Append(StatsSheetCard(sheet));
    RenderStatsAugments();
    if (CalcData())
        RenderCooldownCalculator();
    if (!sheet)
        return;
    auto heading = MakeText(L"技能详情", 15, 0xe0c688);
    heading.Margin({0, 4, 0, 2});
    detailBody.Children().Append(heading);
    auto guide = MakeText(AugmentText(sheet, L"textSource") + L" 技能等级跟随上面冷却计算的等级。", 12, 0x91acbb);
    guide.TextWrapping(TextWrapping::Wrap);
    guide.Margin({0, 0, 0, 4});
    detailBody.Children().Append(guide);
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
           AugmentPageSignature();
}
bool StatsPageShown()
{
    return selectedCard >= 0 && detailTab == L"stats" && detailRoot.Visibility() == Visibility::Visible;
}
// The user's own augment changes show at once, as on the augment page; the add list closes itself first.
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
