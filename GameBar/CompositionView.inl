// Included inside WidgetView. Both teams' composition from arammeta's ARAM model, computed by the main program:
// capability grades (D is a gap), damage mix and front line, and in Mayhem the estimated win rate and the strongest
// pairs. The page states facts only; nothing can be swapped mid-game.
Grid compositionRoot;
StackPanel compositionBody;
ScrollViewer compositionScroll;
Button compositionButton, compositionChatButton;
std::array<MenuFlyoutItem, 2> compositionChatTargets;
JsonObject composition{nullptr};
hstring compositionRevision;
bool compositionOpen = false, supportsCompositionChat = false;
int compositionReturnCard = -1;

void InitializeComposition()
{
    compositionButton = DetailButton(L"阵容");
    compositionButton.Click([this](auto &&, auto &&) { OpenComposition(); });
    ToolTipService::SetToolTip(
        compositionButton,
        box_value(L"大乱斗、海克斯大乱斗中双方的阵容能力、伤害构成与估计胜率（arammeta 统计模型）。"));
    compositionButton.Visibility(Visibility::Collapsed);
    for (int i = 0; i < 2; ++i)
    {
        RowDefinition r;
        r.Height({1, i == 1 ? GridUnitType::Star : GridUnitType::Auto});
        compositionRoot.RowDefinitions().Append(r);
    }
    StackPanel navigation;
    navigation.Orientation(Orientation::Horizontal);
    navigation.Margin({0, 0, 0, 8});
    auto back = DetailButton(L"‹ 返回");
    back.Margin({0, 0, 10, 0});
    back.Click([this](auto &&, auto &&) { CloseComposition(); });
    navigation.Children().Append(back);
    auto title = MakeText(L"阵容分析", 20, 0xe9d8a6);
    title.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(title);
    auto info = MakeText(L"ⓘ", 14, 0x91acbb);
    info.Margin({8, 2, 0, 0});
    info.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(info);
    SetRecordTip(
        navigation,
        L"能力：前排、控制、开团、伤害、消耗、清线、续航七项，按同人数随机阵容排名给出等级，D 为低于短板阈值。\n"
        L"估计胜率（仅海克斯大乱斗）：英雄调整后胜率均值 + 队友搭配的历史提升 + 阵容修正，不保证单场胜负。\n"
        L"数据：arammeta 的大乱斗统计模型，与选人助手的阵容分析相同。");
    MenuFlyout menu;
    std::array<hstring, 2> names{L"我方", L"敌方"};
    std::array<hstring, 2> keys{L"own", L"enemy"};
    for (size_t i = 0; i < compositionChatTargets.size(); ++i)
    {
        compositionChatTargets[i].Text(names[i]);
        compositionChatTargets[i].Click([this, target = keys[i]](auto &&, auto &&)
                                        { QueueChat(target, L"composition"); });
        menu.Items().Append(compositionChatTargets[i]);
    }
    compositionChatButton = DetailButton(L"");
    compositionChatButton.Content(MakeText(L"发送阵容 ▾", 13));
    compositionChatButton.Margin({12, 0, 0, 0});
    compositionChatButton.Flyout(menu);
    ToolTipService::SetToolTip(compositionChatButton,
                               box_value(L"把我方或敌方的七项能力等级、物理伤害占比和前排人数发送到游戏聊天。\n"
                                         L"需以管理员身份运行 League；点击后按 Win＋G 返回游戏。"));
    compositionChatButton.Visibility(Visibility::Collapsed);
    navigation.Children().Append(compositionChatButton);
    compositionRoot.Children().Append(navigation);
    compositionScroll.Content(compositionBody);
    compositionScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
    compositionScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
    Grid::SetRow(compositionScroll, 1);
    compositionRoot.Children().Append(compositionScroll);
    Grid::SetRow(compositionRoot, 2);
    Grid::SetRowSpan(compositionRoot, 2);
    compositionRoot.Visibility(Visibility::Collapsed);
    root.Children().Append(compositionRoot);
}
// The snapshot carries the page only for ARAM-type games once the statistics are read.
void ApplyComposition(JsonObject const &data)
{
    supportsCompositionChat = data.GetNamedBoolean(L"chatComposition", false);
    auto value = data.TryLookup(L"composition");
    composition =
        value && value.ValueType() == JsonValueType::Object && value.GetObject().GetNamedNumber(L"protocol", 0) == 1
            ? value.GetObject()
            : JsonObject{nullptr};
    bool available = composition != nullptr;
    compositionButton.Visibility(available ? Visibility::Visible : Visibility::Collapsed);
    compositionButton.IsEnabled(available && populatedCards > 0);
    if (!available)
    {
        CloseComposition();
        return;
    }
    if (compositionOpen && composition.GetNamedString(L"revision", L"") != compositionRevision)
        RenderComposition();
}
void OpenComposition()
{
    if (!composition || populatedCards == 0)
        return;
    CloseWaves();
    CloseControls();
    CloseAugmentPage();
    compositionReturnCard = selectedCard;
    if (selectedCard >= 0)
        detailRoot.Visibility(Visibility::Collapsed);
    rosterPanel.Visibility(Visibility::Collapsed);
    details.Visibility(Visibility::Collapsed);
    compositionRoot.Visibility(Visibility::Visible);
    compositionOpen = true;
    compositionScroll.ChangeView(nullptr, 0.0, nullptr);
    RenderComposition();
    UpdateChatButtons();
}
// Back returns to the roster or the hero that was open, as the other pages do.
void CloseComposition()
{
    if (!compositionOpen)
        return;
    compositionOpen = false;
    compositionRevision = L"";
    compositionBody.Children().Clear();
    compositionRoot.Visibility(Visibility::Collapsed);
    if (compositionReturnCard >= 0 && compositionReturnCard == selectedCard)
        detailRoot.Visibility(Visibility::Visible);
    else
    {
        rosterPanel.Visibility(Visibility::Visible);
        UpdateRosterGuide();
    }
    compositionReturnCard = -1;
}
void RenderComposition()
{
    compositionBody.Children().Clear();
    if (!composition)
        return;
    compositionRevision = composition.GetNamedString(L"revision", L"");
    auto note = MakeText(composition.GetNamedString(L"note", L""), 12, 0x91acbb);
    note.TextWrapping(TextWrapping::Wrap);
    note.Margin({0, 0, 0, 8});
    compositionBody.Children().Append(note);
    Grid teamsGrid;
    auto teamsData = composition.GetNamedArray(L"teams", JsonArray());
    for (uint32_t i = 0; i < teamsData.Size() && i < 2; ++i)
    {
        teamsGrid.ColumnDefinitions().Append(ColumnDefinition());
        auto card = CompositionTeamCard(teamsData.GetObjectAt(i), i == 0 ? 0x45c7ce : 0xf3b37e);
        Grid::SetColumn(card, static_cast<int32_t>(i));
        teamsGrid.Children().Append(card);
    }
    compositionBody.Children().Append(teamsGrid);
}
Border CompositionTeamCard(JsonObject const &team, uint32_t accent)
{
    StackPanel content;
    StackPanel heading;
    heading.Orientation(Orientation::Horizontal);
    heading.Children().Append(MakeText(team.GetNamedString(L"title", L""), 16, accent));
    auto winRate = team.GetNamedString(L"winRate", L"");
    if (!winRate.empty())
    {
        auto rate = MakeText(winRate, 16, 0xe0c688);
        rate.Margin({12, 0, 0, 0});
        heading.Children().Append(rate);
    }
    content.Children().Append(heading);
    auto detail = team.GetNamedString(L"winDetail", L"");
    if (!detail.empty())
    {
        auto line = MakeText(detail, 11, 0x91acbb);
        line.TextWrapping(TextWrapping::Wrap);
        line.Margin({0, 2, 0, 0});
        content.Children().Append(line);
    }
    StackPanel capabilities;
    capabilities.Margin({0, 8, 0, 6});
    for (auto const &value : team.GetNamedArray(L"capabilities", JsonArray()))
        capabilities.Children().Append(CapabilityBar(value.GetObject(), accent));
    content.Children().Append(capabilities);
    content.Children().Append(MakeText(team.GetNamedString(L"damage", L""), 12, 0xe2eef3));
    auto lacks = team.GetNamedString(L"lacks", L"");
    auto lackLine =
        MakeText(lacks.empty() ? hstring(L"短板：无") : L"短板：" + lacks, 12, lacks.empty() ? 0x91acbb : 0xff8a7a);
    lackLine.Margin({0, 2, 0, 0});
    content.Children().Append(lackLine);
    auto missing = team.GetNamedString(L"missing", L"");
    if (!missing.empty())
    {
        auto line = MakeText(L"缺少统计：" + missing + L"，未计入。", 11, 0xe0b25c);
        line.TextWrapping(TextWrapping::Wrap);
        content.Children().Append(line);
    }
    auto pairs = team.GetNamedArray(L"pairs", JsonArray());
    if (pairs.Size() > 0)
    {
        auto title = MakeText(L"队友搭配", 12, 0x91acbb);
        title.Margin({0, 8, 0, 2});
        content.Children().Append(title);
        for (auto const &pair : pairs)
        {
            auto line = MakeText(pair.GetString(), 12, 0xc0d5df);
            line.TextWrapping(TextWrapping::Wrap);
            content.Children().Append(line);
        }
    }
    return RecordCard(content, accent);
}
// Name, a bar filled to the percentile among random teams of the same size, and the grade; gaps in red.
Grid CapabilityBar(JsonObject const &capability, uint32_t accent)
{
    bool lack = capability.GetNamedBoolean(L"lack", false);
    double share = (std::clamp)(capability.GetNamedNumber(L"percentile", 0), 0.0, 1.0);
    Grid row;
    row.Margin({0, 2, 0, 2});
    for (auto width :
         {GridLength{40, GridUnitType::Pixel}, GridLength{1, GridUnitType::Star}, GridLength{28, GridUnitType::Pixel}})
    {
        ColumnDefinition column;
        column.Width(width);
        row.ColumnDefinitions().Append(column);
    }
    auto name = MakeText(capability.GetNamedString(L"name", L""), 12, 0xa4bcc7);
    name.VerticalAlignment(VerticalAlignment::Center);
    row.Children().Append(name);
    // Two star columns split the track by the share, so the bar scales with the card.
    Grid track;
    track.Height(8);
    track.VerticalAlignment(VerticalAlignment::Center);
    track.Background(MakeBrush(0x143542));
    ColumnDefinition filled, rest;
    filled.Width({(std::max)(share, 0.001), GridUnitType::Star});
    rest.Width({(std::max)(1 - share, 0.001), GridUnitType::Star});
    track.ColumnDefinitions().Append(filled);
    track.ColumnDefinitions().Append(rest);
    Border fill;
    fill.Background(MakeBrush(lack ? 0xff8a7a : accent));
    track.Children().Append(fill);
    Grid::SetColumn(track, 1);
    row.Children().Append(track);
    auto grade = MakeText(capability.GetNamedString(L"grade", L""), 13, lack ? 0xff8a7a : 0xe2eef3);
    grade.HorizontalAlignment(HorizontalAlignment::Right);
    grade.VerticalAlignment(VerticalAlignment::Center);
    Grid::SetColumn(grade, 2);
    row.Children().Append(grade);
    SetRecordTip(row, capability.GetNamedString(L"tip", L""));
    return row;
}
