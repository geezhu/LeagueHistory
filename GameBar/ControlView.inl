// Included inside WidgetView. Each champion's crowd control abilities (recognized from the client's ability text) and
// the survival items the Tab scoreboard shows. The page lists facts in roster order; it never ranks or picks targets.
Grid controlRoot;
StackPanel controlBody;
ScrollViewer controlScroll;
Button controlButton;
JsonArray controlRows;
bool controlOpen = false, supportsControls = false;
int controlReturnCard = -1;
// New survival items play the item's own sound for the teams chosen here (the own team includes the player); the
// main program plays it, since Windows mutes this widget while the game has focus. Turning a team on plays nothing:
// a sample in a real item's sound was mistaken for a purchase.
Button survivalSoundButton;
ToggleMenuFlyoutItem enemySoundItem, allySoundItem;
bool enemySound = true, allySound = false, controlSettingsWritten = false, controlSettingsWriting = false;
hstring pendingControlSettings;
static constexpr wchar_t const *SurvivalSoundSetting = L"survivalItemSound";
static constexpr wchar_t const *AllySoundSetting = L"survivalItemAllySound";
// Sends one side's recognized control skills to game chat through the shared send gate.
Button controlChatButton;
std::array<MenuFlyoutItem, 2> controlChatTargets;

void InitializeControls()
{
    controlButton = DetailButton(L"控制保命");
    controlButton.Click([this](auto &&, auto &&) { OpenControls(); });
    ToolTipService::SetToolTip(controlButton, box_value(L"双方英雄的关键控制技能与保命装备。"));
    controlButton.Visibility(Visibility::Collapsed);
    for (int i = 0; i < 2; ++i)
    {
        RowDefinition r;
        r.Height({1, i == 1 ? GridUnitType::Star : GridUnitType::Auto});
        controlRoot.RowDefinitions().Append(r);
    }
    StackPanel navigation;
    navigation.Orientation(Orientation::Horizontal);
    navigation.Margin({0, 0, 0, 8});
    auto back = DetailButton(L"‹ 返回");
    back.Margin({0, 0, 10, 0});
    back.Click([this](auto &&, auto &&) { CloseControls(); });
    navigation.Children().Append(back);
    auto title = MakeText(L"控制保命", 20, 0xe9d8a6);
    title.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(title);
    auto info = MakeText(L"ⓘ", 14, 0x91acbb);
    info.Margin({8, 2, 0, 0});
    info.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(info);
    SetRecordTip(navigation,
                 L"控制：按客户端技能说明识别的硬控（不含致盲），说明没写到的效果可能遗漏。\n"
                 L"减速：施法距离 ≥ 600、满级减速 ≥ 40% 的远程强减速，数值来自 CommunityDragon 整理的游戏数据。\n"
                 L"保命：Tab 计分板里的保命装备；2999 不提供装备冷却，已经用过的也会显示。\n"
                 L"生命：自己取 2999 的实际属性；其他玩家按等级基础属性 + 装备 + 已添加的坦克引擎估算，"
                 L"看不到其他海克斯、碎片和叠层装备，实际通常更高。\n"
                 L"海克斯：在「海克斯」页记录，这里只显示；生命和覆盖率按记录计算。\n"
                 L"保命提示音：做出保命装备时播放该装备的游戏音效，可分别选择敌方、我方（含自己）。 "
                 L"召唤师提示音：我方卡片上手动开始的召唤师技能计时结束时提示；敌方召唤师技能只显示图标，不计时。");
    InitializeSurvivalSound(navigation);
    InitializeSummonerSound(navigation);
    InitializeControlChat(navigation);
    controlRoot.Children().Append(navigation);
    controlScroll.Content(controlBody);
    controlScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
    controlScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
    Grid::SetRow(controlScroll, 1);
    controlRoot.Children().Append(controlScroll);
    Grid::SetRow(controlRoot, 2);
    Grid::SetRowSpan(controlRoot, 2);
    controlRoot.Visibility(Visibility::Collapsed);
    root.Children().Append(controlRoot);
}
void InitializeSurvivalSound(StackPanel const &navigation)
{
    auto values = ApplicationData::Current().LocalSettings().Values();
    auto enemy = values.TryLookup(SurvivalSoundSetting);
    auto ally = values.TryLookup(AllySoundSetting);
    enemySound = !enemy || unbox_value_or<bool>(enemy, true);
    allySound = ally && unbox_value_or<bool>(ally, false);
    MenuFlyout menu;
    enemySoundItem.Text(L"敌方");
    allySoundItem.Text(L"我方（含自己）");
    for (auto item : {enemySoundItem, allySoundItem})
    {
        item.Click([this](auto &&, auto &&) { ChangeSurvivalSound(); });
        menu.Items().Append(item);
    }
    survivalSoundButton = DetailButton(L"");
    survivalSoundButton.Margin({12, 0, 0, 0});
    survivalSoundButton.Flyout(menu);
    ToolTipService::SetToolTip(
        survivalSoundButton,
        box_value(L"做出保命装备时播放该装备的游戏音效（取自英雄联盟官方维基），可分别选择敌方、我方。"
                  L"2999 看不到装备冷却和使用，所以提示的是做出装备，不是使用装备。"));
    RefreshSurvivalSoundButton();
    navigation.Children().Append(survivalSoundButton);
}
void InitializeControlChat(StackPanel const &navigation)
{
    MenuFlyout menu;
    std::array<hstring, 2> names{L"我方", L"敌方"};
    std::array<hstring, 2> keys{L"own", L"enemy"};
    for (size_t i = 0; i < controlChatTargets.size(); ++i)
    {
        controlChatTargets[i].Text(names[i]);
        controlChatTargets[i].Click([this, target = keys[i]](auto &&, auto &&) { QueueChat(target, L"controls"); });
        menu.Items().Append(controlChatTargets[i]);
    }
    controlChatButton = DetailButton(L"");
    controlChatButton.Content(MakeText(L"发送控制 ▾", 13));
    controlChatButton.Margin({12, 0, 0, 0});
    controlChatButton.Flyout(menu);
    ToolTipService::SetToolTip(controlChatButton,
                               box_value(L"把我方或敌方每个英雄的控制技能（含远程强减速）发送到游戏聊天。\n"
                                         L"需以管理员身份运行 League；点击后按 Win＋G 返回游戏。"));
    controlChatButton.Visibility(Visibility::Collapsed);
    navigation.Children().Append(controlChatButton);
}
void ChangeSurvivalSound()
{
    enemySound = enemySoundItem.IsChecked();
    allySound = allySoundItem.IsChecked();
    auto values = ApplicationData::Current().LocalSettings().Values();
    values.Insert(SurvivalSoundSetting, box_value(enemySound));
    values.Insert(AllySoundSetting, box_value(allySound));
    RefreshSurvivalSoundButton();
    SaveControlSettings();
}
void RefreshSurvivalSoundButton()
{
    enemySoundItem.IsChecked(enemySound);
    allySoundItem.IsChecked(allySound);
    hstring state = enemySound && allySound ? L"双方" : enemySound ? L"敌方" : allySound ? L"我方" : L"关";
    survivalSoundButton.Content(MakeText(L"保命提示音：" + state + L" ▾", 13));
}
void SaveControlSettings()
{
    if (!widget || fixturePreview || stopped)
        return;
    controlSettingsWritten = true;
    JsonObject settings;
    settings.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    settings.Insert(L"survivalSound", JsonValue::CreateBooleanValue(enemySound));
    settings.Insert(L"allySound", JsonValue::CreateBooleanValue(allySound));
    settings.Insert(L"summonerSound", JsonValue::CreateBooleanValue(summonerSound));
    pendingControlSettings = settings.Stringify();
    WriteControlSettings();
}
fire_and_forget WriteControlSettings()
{
    if (controlSettingsWriting || stopped)
        co_return;
    auto lifetime = shared_from_this();
    controlSettingsWriting = true;
    try
    {
        while (!pendingControlSettings.empty() && !stopped)
        {
            auto value = pendingControlSettings;
            pendingControlSettings = L"";
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                L"control-settings.tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, value);
            co_await file.RenameAsync(L"control-settings.json", NameCollisionOption::ReplaceExisting);
        }
    }
    catch (hresult_error const &e)
    {
        // The main program keeps the last setting it read; the next change writes the file again.
        Trace(L"control-settings-write-error " + e.message());
    }
    controlSettingsWriting = false;
}
void ApplyControls(JsonObject const &data, JsonArray const &rows)
{
    supportsControls = data.GetNamedNumber(L"controlProtocol", 0) == 1;
    // The main program defaults to sound on; the stored choice reaches it once a snapshot shows it is listening.
    if (supportsControls && !controlSettingsWritten)
        SaveControlSettings();
    controlRows = rows;
    controlButton.Visibility(supportsControls ? Visibility::Visible : Visibility::Collapsed);
    controlButton.IsEnabled(supportsControls && populatedCards > 0);
    if (controlOpen)
        RenderControls();
}
void OpenControls()
{
    if (!supportsControls || populatedCards == 0)
        return;
    CloseWaves();
    CloseAugmentPage();
    CloseComposition();
    controlReturnCard = selectedCard;
    if (selectedCard >= 0)
        detailRoot.Visibility(Visibility::Collapsed);
    rosterPanel.Visibility(Visibility::Collapsed);
    details.Visibility(Visibility::Collapsed);
    controlRoot.Visibility(Visibility::Visible);
    controlOpen = true;
    controlScroll.ChangeView(nullptr, 0.0, nullptr);
    RenderControls();
}
// The layout check captures once every listed champion's roster icon has decoded.
bool ControlIconsReady()
{
    for (auto const &card : cards)
        if (card.hero > 0 && !card.icon.Source())
            return false;
    return true;
}
// Back returns to the roster or the hero that was open, as the wave page does.
void CloseControls()
{
    if (!controlOpen)
        return;
    controlOpen = false;
    controlBody.Children().Clear();
    controlRoot.Visibility(Visibility::Collapsed);
    if (controlReturnCard >= 0 && controlReturnCard == selectedCard)
        detailRoot.Visibility(Visibility::Visible);
    else
    {
        rosterPanel.Visibility(Visibility::Visible);
        UpdateRosterGuide();
    }
    controlReturnCard = -1;
}
// Enemies first, in roster order; the snapshot lists the own team in slots 0-4.
void RenderControls()
{
    controlBody.Children().Clear();
    for (int team : {1, 0})
    {
        auto heading = MakeText(teams[team].Text(), 15, team == 1 ? 0xf3b37e : 0x45c7ce);
        heading.Margin({0, team == 1 ? 0.0 : 8.0, 0, 6});
        controlBody.Children().Append(heading);
        for (uint32_t slot = team * 5; slot < team * 5u + 5 && slot < controlRows.Size(); ++slot)
        {
            auto row = controlRows.GetObjectAt(slot);
            if (row.GetNamedNumber(L"championId", 0) > 0)
                controlBody.Children().Append(CreateControlRow(slot, row, team == 1 ? 0xf3b37e : 0x45c7ce));
        }
    }
}
Border CreateControlRow(uint32_t slot, JsonObject const &row, uint32_t accent)
{
    Grid layout;
    ColumnDefinition avatar, details;
    avatar.Width({1, GridUnitType::Auto});
    details.Width({1, GridUnitType::Star});
    layout.ColumnDefinitions().Append(avatar);
    layout.ColumnDefinitions().Append(details);
    Border frame;
    frame.Width(36);
    frame.Height(36);
    frame.CornerRadius({4, 4, 4, 4});
    frame.Background(MakeBrush(0x1d4c57));
    frame.VerticalAlignment(VerticalAlignment::Center);
    Image image;
    image.Stretch(Stretch::UniformToFill);
    image.Source(cards[slot].icon.Source());
    frame.Child(image);
    layout.Children().Append(frame);
    StackPanel text;
    text.Margin({10, 0, 0, 0});
    Grid::SetColumn(text, 1);
    StackPanel header;
    header.Orientation(Orientation::Horizontal);
    header.Children().Append(MakeText(row.GetNamedString(L"champion", L""), 14));
    auto player = MakeText(row.GetNamedString(L"name", L""), 11, 0x91acbb);
    player.Margin({8, 2, 0, 0});
    header.Children().Append(player);
    text.Children().Append(header);
    text.Children().Append(ControlLine(L"控制", ControlChips(row)));
    text.Children().Append(ControlLine(L"保命", SurvivalChips(row)));
    text.Children().Append(ControlLine(L"生命", HealthChips(row)));
    if (supportsAugments)
        text.Children().Append(ControlLine(L"海克斯", AugmentSummaryChips(cards[slot].key)));
    layout.Children().Append(text);
    return RecordCard(layout, accent);
}
static Border ControlChip(hstring const &text, uint32_t color, hstring const &tip)
{
    Border chip;
    chip.Background(MakeBrush(0x143542));
    chip.BorderBrush(MakeBrush(color));
    // Thickness{1} would set only the left edge.
    chip.BorderThickness({1, 1, 1, 1});
    chip.CornerRadius({3, 3, 3, 3});
    chip.Padding({6, 1, 6, 2});
    chip.Margin({0, 0, 4, 0});
    chip.Child(MakeText(text, 12, color));
    SetRecordTip(chip, tip);
    return chip;
}
static StackPanel ControlLine(hstring const &label, std::vector<UIElement> const &items)
{
    StackPanel line;
    line.Orientation(Orientation::Horizontal);
    line.Margin({0, 4, 0, 0});
    auto name = MakeText(label, 11, 0x91acbb);
    name.Width(38);
    name.VerticalAlignment(VerticalAlignment::Center);
    line.Children().Append(name);
    for (auto const &item : items)
        line.Children().Append(item);
    return line;
}
// Displacement and disables in red, roots and pulls in amber, silence in teal.
static uint32_t ControlColor(std::wstring_view effects)
{
    auto has = [effects](wchar_t const *word) { return effects.find(word) != std::wstring_view::npos; };
    for (auto hard : {L"击飞", L"压制", L"晕眩", L"魅惑", L"嘲讽", L"恐惧", L"沉睡", L"变形"})
        if (has(hard))
            return 0xff8a7a;
    if (has(L"禁锢") || has(L"拉拽") || has(L"击退"))
        return 0xe0b25c;
    if (has(L"沉默"))
        return 0x65d6d6;
    return 0x7fb2ff;
}
// The main program picks the rank the champion's level allows and explains it, durations included.
static hstring SlowTiming(JsonObject const &skill)
{
    auto note = skill.GetNamedString(L"slowNote", L"");
    return note.empty() ? note : L"\n" + note;
}
static std::vector<UIElement> ControlChips(JsonObject const &row)
{
    std::vector<UIElement> chips;
    auto value = row.TryLookup(L"controls");
    if (!value || value.ValueType() != JsonValueType::Array)
    {
        chips.push_back(MakeText(L"读取中…", 12, 0x6f8793));
        return chips;
    }
    for (auto const &entry : value.GetArray())
    {
        auto skill = entry.GetObject();
        auto effects = skill.GetNamedString(L"effects", L"");
        auto key = skill.GetNamedString(L"key", L"");
        auto range = static_cast<int>(skill.GetNamedNumber(L"range", 0));
        chips.push_back(ControlChip(key + L" " + effects,
                                    ControlColor(effects),
                                    key + L" · " + skill.GetNamedString(L"name", L"") +
                                        (range > 0 ? L" · 施法距离 " + to_hstring(range) : hstring()) +
                                        SlowTiming(skill) + L"\n" + skill.GetNamedString(L"description", L"")));
    }
    if (chips.empty())
        chips.push_back(MakeText(L"未识别到硬控", 12, 0x6f8793));
    return chips;
}
// The player's real health from 2999, everyone else's estimate; the tooltip says what the estimate includes.
static std::vector<UIElement> HealthChips(JsonObject const &row)
{
    auto health = row.GetNamedString(L"health", L"");
    if (health.empty())
        return {MakeText(row.HasKey(L"survival") ? L"读取英雄属性中…" : L"进入对局后显示", 12, 0x6f8793)};
    auto text = MakeText(health, 12, 0xe2eef3);
    text.VerticalAlignment(VerticalAlignment::Center);
    SetRecordTip(text, row.GetNamedString(L"healthDetails", L""));
    return {text};
}
static uint32_t SurvivalColor(hstring const &effect)
{
    if (effect == L"凝滞" || effect == L"复活")
        return 0xe0c688;
    if (effect == L"解控")
        return 0x65d6d6;
    if (effect == L"法术护盾")
        return 0xb8a2ff;
    return 0xa4bcc7;
}
static std::vector<UIElement> SurvivalChips(JsonObject const &row)
{
    std::vector<UIElement> chips;
    auto value = row.TryLookup(L"survival");
    if (!value || value.ValueType() != JsonValueType::Array)
    {
        chips.push_back(MakeText(L"进入对局后显示", 12, 0x6f8793));
        return chips;
    }
    for (auto const &entry : value.GetArray())
    {
        auto item = entry.GetObject();
        auto effect = item.GetNamedString(L"effect", L"");
        chips.push_back(ControlChip(item.GetNamedString(L"name", L""),
                                    SurvivalColor(effect),
                                    effect + L" · " + item.GetNamedString(L"rule", L"") +
                                        L"\n2999 不提供装备冷却，已经用过的也会显示。"));
    }
    if (chips.empty())
        chips.push_back(MakeText(L"无", 12, 0x6f8793));
    return chips;
}
