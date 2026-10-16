// Included inside WidgetView. The detail page stays in the Game Bar-owned view.
Grid detailRoot;
StackPanel detailBody, detailTabs;
ScrollViewer detailScroll;
TextBlock detailTitle = MakeText(L"英雄详情", 22, 0xe9d8a6);
TextBlock detailStats = MakeText(L"", 13, 0x65d6d6);
TextBlock detailMastery = MakeText(L"", 12, 0xdbc47f);
TextBlock detailSource = MakeText(L"", 12, 0xa4bcc7);
Image detailArtwork;
std::array<Button, 10> heroButtons;
std::array<Image, 10> heroIcons;
std::array<Border, 10> heroIndicators;
std::array<Button, 7> tabButtons;
static constexpr std::array<wchar_t const *, 7> DetailTabKeys{
    L"skills", L"builds", L"runes", L"augments", L"live", L"cooldowns", L"record"};
JsonObject detailData;
hstring detailRequest, detailRevision, detailTab = L"skills";
hstring pendingDetailRequest;
int64_t detailRequestedAt = 0;
bool detailBusy = false, requestBusy = false, supportsDetails = false;
bool fixturePreview = false, fixtureOpened = false;
int pendingDetailImages = 0;
int detailQueue = 0;
int detailRarity = 0;
// The augment tab's search spans all three rarities. The box lives across renders and typing redraws only the
// results below it, so it keeps focus.
TextBox augmentSearch{nullptr};
StackPanel augmentResults;
uint64_t detailRender = 0;

static hstring NewDetailId()
{
    GUID value{};
    check_hresult(CoCreateGuid(&value));
    return to_hstring(guid(value));
}
void ConfigureDetailPreview(hstring const &argument)
{
    std::wstring_view text(argument), prefix = L"--capture-detail-fixture:";
    if (!text.starts_with(prefix))
        return;
    auto tab = text.substr(prefix.size());
    if (tab != L"skills" && tab != L"builds" && tab != L"runes" && tab != L"augments" && tab != L"live" &&
        tab != L"record" && tab != L"roster" && tab != L"voice" && tab != L"waves" && tab != L"marks" &&
        tab != L"controls" && tab != L"cooldowns" && tab != L"augmentpage")
        return;
    fixturePreview = true;
    fixtureOpened = false;
    Clear();
    revision = L"";
    detailTab = hstring(tab);
    capturePending = true;
}

static Button DetailButton(hstring const &text)
{
    Button b;
    b.Content(MakeText(text, 13));
    b.Background(MakeBrush(0x143542));
    b.BorderBrush(MakeBrush(0x315662));
    b.BorderThickness({1});
    b.Padding({12, 6, 12, 6});
    return b;
}
void InitializeDetails()
{
    for (int i = 0; i < 4; ++i)
    {
        RowDefinition r;
        r.Height({1, i == 3 ? GridUnitType::Star : GridUnitType::Auto});
        detailRoot.RowDefinitions().Append(r);
    }
    InitializeDetailNavigation();
    InitializeDetailBanner();
    InitializeDetailTabs();
    detailScroll.Content(detailBody);
    detailScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
    detailScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
    Grid::SetRow(detailScroll, 3);
    detailRoot.Children().Append(detailScroll);
    Grid::SetRow(detailRoot, 2);
    Grid::SetRowSpan(detailRoot, 2);
    detailRoot.Visibility(Visibility::Collapsed);
    root.Children().Append(detailRoot);
}
void InitializeDetailNavigation()
{
    StackPanel navigation;
    navigation.Orientation(Orientation::Horizontal);
    navigation.Margin({0, 0, 0, 8});
    auto back = DetailButton(L"‹ 返回");
    back.Margin({0, 0, 8, 0});
    back.Click([this](auto &&, auto &&) { CloseDetails(true); });
    navigation.Children().Append(back);
    for (int i = 0; i < 10; ++i)
    {
        if (i % 5 == 0)
        {
            auto team = MakeText(i == 0 ? L"我方" : L"敌方", 12, i == 0 ? 0x45c7ce : 0xf3b37e);
            team.VerticalAlignment(VerticalAlignment::Center);
            team.Margin({0, 0, 4, 0});
            navigation.Children().Append(team);
        }
        auto &b = heroButtons[i];
        b = DetailButton(L"");
        b.Padding({2});
        b.Margin({0, 0, i == 4 ? 10.0 : 3.0, 0});
        heroIcons[i].Width(32);
        heroIcons[i].Height(32);
        // A bar under the icon marks the open hero; button borders are partly hidden by the artwork.
        heroIndicators[i].Height(3);
        heroIndicators[i].Margin({0, 2, 0, 0});
        StackPanel icon;
        icon.Children().Append(heroIcons[i]);
        icon.Children().Append(heroIndicators[i]);
        b.Content(icon);
        b.Click([this, i](auto &&, auto &&) { OpenDetails(i); });
        navigation.Children().Append(b);
    }
    auto retry = DetailButton(L"重试");
    retry.Margin({6, 0, 0, 0});
    retry.Click(
        [this](auto &&, auto &&)
        {
            if (selectedCard >= 0)
                OpenDetails(selectedCard);
        });
    navigation.Children().Append(retry);
    detailRoot.Children().Append(navigation);
}
void InitializeDetailBanner()
{
    Grid banner;
    banner.Height(128);
    banner.Background(MakeBrush(0x102f3c));
    detailArtwork.Stretch(Stretch::UniformToFill);
    detailArtwork.Opacity(.36);
    banner.Children().Append(detailArtwork);
    StackPanel summary;
    summary.Margin({14, 8, 14, 8});
    summary.Children().Append(detailTitle);
    detailStats.Margin({0, 3, 0, 3});
    summary.Children().Append(detailStats);
    summary.Children().Append(detailMastery);
    detailSource.TextWrapping(TextWrapping::Wrap);
    summary.Children().Append(detailSource);
    banner.Children().Append(summary);
    Grid::SetRow(banner, 1);
    detailRoot.Children().Append(banner);
}
void InitializeDetailTabs()
{
    detailTabs.Orientation(Orientation::Horizontal);
    detailTabs.Margin({0, 8, 0, 8});
    // The player's record follows the hero tabs: it describes the account, not the champion.
    std::array<hstring, 7> names{L"技能", L"出装", L"符文", L"海克斯", L"本局", L"冷却", L"战绩"};
    for (size_t i = 0; i < names.size(); ++i)
    {
        auto &b = tabButtons[i];
        b = DetailButton(names[i]);
        b.Margin({0, 0, 6, 0});
        b.MinWidth(80);
        b.Click(
            [this, key = hstring(DetailTabKeys[i])](auto &&, auto &&)
            {
                detailTab = key;
                RenderDetails();
            });
        detailTabs.Children().Append(b);
    }
    Grid::SetRow(detailTabs, 2);
    detailRoot.Children().Append(detailTabs);
}
void RefreshDetailHeader()
{
    for (int i = 0; i < 10; ++i)
    {
        heroIcons[i].Source(cards[i].icon.Source());
        heroIndicators[i].Background(i == selectedCard ? MakeBrush(0x7ff0ee) : MakeBrush(i < 5 ? 0x1d4c57 : 0x4a3b28));
        heroButtons[i].IsEnabled(cards[i].hero > 0);
        // Selection outranks party color; team tint separates allies from enemies at a glance.
        bool selected = i == selectedCard;
        heroButtons[i].Background(MakeBrush(selected ? 0x1f5a66 : i < 5 ? 0x12343e : 0x352c22));
        heroButtons[i].BorderBrush(MakeBrush(selected              ? 0x7ff0ee
                                             : cards[i].partyColor ? cards[i].partyColor
                                                                   : 0x315662));
        heroButtons[i].BorderThickness({selected ? 3.0 : cards[i].partyColor ? 2.0 : 1.0});
        ToolTipService::SetToolTip(heroButtons[i],
                                   box_value(teams[i / 5].Text() + L" · " + cards[i].champion.Text() +
                                             (cards[i].partyLabel.empty() ? L"" : L" · " + cards[i].partyLabel)));
    }
    RefreshMarkButton();
    if (selectedCard < 0)
        return;
    detailTitle.Text(cards[selectedCard].champion.Text() + L"  " + cards[selectedCard].tier.Text());
    detailStats.Text(cards[selectedCard].championStats + (cards[selectedCard].deathStreakText.empty()
                                                              ? L""
                                                              : L" · " + cards[selectedCard].deathStreakText));
    detailMastery.Text(cards[selectedCard].player.Text() + L" · " + cards[selectedCard].mastery.Text() +
                       (cards[selectedCard].partyLabel.empty() ? L"" : L" · " + cards[selectedCard].partyLabel));
    detailMastery.Foreground(MakeBrush(cards[selectedCard].partyColor ? cards[selectedCard].partyColor : 0xdbc47f));
    ToolTipService::SetToolTip(
        detailMastery,
        box_value(cards[selectedCard].masteryDetails +
                  (cards[selectedCard].partyDetails.empty() ? L"" : L"\n" + cards[selectedCard].partyDetails)));
    ToolTipService::SetToolTip(detailStats,
                               box_value(cards[selectedCard].details + L"\n" + cards[selectedCard].deathStreakDetails));
    bool mayhem = detailQueue == 2400 || detailQueue == 3270;
    tabButtons[2].Visibility(mayhem ? Visibility::Collapsed : Visibility::Visible);
    tabButtons[3].Visibility(mayhem ? Visibility::Visible : Visibility::Collapsed);
    tabButtons[4].Visibility(HasLivePanel() ? Visibility::Visible : Visibility::Collapsed);
    tabButtons[5].Visibility(HasCalculator() ? Visibility::Visible : Visibility::Collapsed);
}
// The in-game panel describes the active player only; 2999 exposes no other player's attributes.
bool HasLivePanel()
{
    return selectedCard >= 0 && cards[selectedCard].self && liveSections.Size() > 0;
}
void OpenDetails(int index)
{
    if (index < 0 || index >= 10 || cards[index].hero <= 0)
        return;
    if (!supportsDetails)
    {
        details.Text(L"当前主程序尚不支持英雄详情，请启动新版 League.exe。");
        details.Visibility(Visibility::Visible);
        return;
    }
    selectedCard = index;
    detailData = JsonObject();
    detailRevision = L"";
    detailRequest = NewDetailId();
    detailRequestedAt = Now();
    detailArtwork.Source(nullptr);
    detailSource.Text(L"正在读取英雄详情…");
    if ((detailQueue == 2400 || detailQueue == 3270) && detailTab == L"runes")
        detailTab = L"augments";
    else if (detailQueue != 2400 && detailQueue != 3270 && detailTab == L"augments")
        detailTab = L"skills";
    // A layout fixture keeps the tab it was asked to capture.
    if (HasLivePanel())
    {
        if (!fixturePreview)
            detailTab = L"live";
    }
    else if (detailTab == L"live")
        detailTab = L"skills";
    if (detailTab == L"cooldowns" && !HasCalculator())
        detailTab = L"skills";
    rosterPanel.Visibility(Visibility::Collapsed);
    details.Visibility(Visibility::Collapsed);
    detailRoot.Visibility(Visibility::Visible);
    RefreshDetailHeader();
    RenderDetails();
    QueueDetailRequest(index, cards[index].hero, detailRequest);
    UpdateChatButtons();
}
void CloseDetails(bool notify)
{
    ++detailRender;
    selectedCard = -1;
    detailRequest = L"";
    detailRevision = L"";
    detailData = JsonObject();
    detailArtwork.Source(nullptr);
    detailBody.Children().Clear();
    detailRoot.Visibility(Visibility::Collapsed);
    rosterPanel.Visibility(Visibility::Visible);
    UpdateRosterGuide();
    if (notify && supportsDetails)
        QueueDetailRequest(-1, 0, NewDetailId());
    UpdateChatButtons();
}
void QueueDetailRequest(int slot, int hero, hstring id)
{
    if (fixturePreview)
        return;
    JsonObject request;
    request.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    request.Insert(L"id", JsonValue::CreateStringValue(id));
    request.Insert(L"session", JsonValue::CreateStringValue(session));
    request.Insert(L"slot", JsonValue::CreateNumberValue(slot));
    request.Insert(L"championId", JsonValue::CreateNumberValue(hero));
    request.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(Now())));
    pendingDetailRequest = request.Stringify();
    WriteDetailRequest();
}
fire_and_forget WriteDetailRequest()
{
    if (requestBusy || stopped)
        co_return;
    auto lifetime = shared_from_this();
    requestBusy = true;
    while (!pendingDetailRequest.empty() && !stopped)
    {
        auto json = pendingDetailRequest;
        pendingDetailRequest = L"";
        try
        {
            auto folder = ApplicationData::Current().LocalFolder();
            auto file =
                co_await folder.CreateFileAsync(L"detail-request.tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, json);
            co_await file.RenameAsync(L"detail-request.json", NameCollisionOption::ReplaceExisting);
        }
        catch (hresult_error const &)
        {
            if (!stopped)
            {
                detailSource.Text(L"无法请求英雄详情，请重试。");
            }
        }
    }
    requestBusy = false;
}
fire_and_forget DecodeDetailImage(Image target, hstring data, uint64_t render, int width = 48)
{
    auto lifetime = shared_from_this();
    if (data.empty())
        co_return;
    ++pendingDetailImages;
    try
    {
        auto buffer = Windows::Security::Cryptography::CryptographicBuffer::DecodeFromBase64String(data);
        if (buffer.Length() > 400000)
        {
            --pendingDetailImages;
            co_return;
        }
        InMemoryRandomAccessStream stream;
        co_await stream.WriteAsync(buffer);
        stream.Seek(0);
        BitmapImage bitmap;
        bitmap.DecodePixelWidth(width);
        co_await bitmap.SetSourceAsync(stream);
        if (!stopped && render == detailRender)
            target.Source(bitmap);
    }
    catch (...)
    {
        // An invalid or missing icon leaves its text in place and must not stop other detail rows.
    }
    --pendingDetailImages;
}
Grid DetailIcons(JsonArray const &icons)
{
    Grid grid;
    grid.HorizontalAlignment(HorizontalAlignment::Left);
    grid.Margin({0, 5, 0, 5});
    for (int col = 0; col < 9; ++col)
    {
        ColumnDefinition c;
        c.Width({48, GridUnitType::Pixel});
        grid.ColumnDefinitions().Append(c);
    }
    for (uint32_t row = 0; row < (icons.Size() + 8) / 9; ++row)
    {
        RowDefinition r;
        r.Height({48, GridUnitType::Pixel});
        grid.RowDefinitions().Append(r);
    }
    for (uint32_t i = 0; i < icons.Size() && i < 27; ++i)
    {
        auto icon = icons.GetObjectAt(i);
        auto name = icon.GetNamedString(L"name", L"图标");
        Grid tile;
        tile.Background(MakeBrush(0x1b414e));
        tile.Margin({0, 0, 4, 4});
        // Owned items in the build comparison stay visible but recede behind the missing ones.
        if (icon.GetNamedBoolean(L"owned", false))
            tile.Opacity(.4);
        auto placeholder =
            MakeText(hstring(std::wstring_view(name).substr(0, std::min<size_t>(2, name.size()))), 11, 0x91acbb);
        placeholder.HorizontalAlignment(HorizontalAlignment::Center);
        placeholder.VerticalAlignment(VerticalAlignment::Center);
        tile.Children().Append(placeholder);
        Image image;
        image.Width(40);
        image.Height(40);
        tile.Children().Append(image);
        DecodeDetailImage(image, icon.GetNamedString(L"image", L""), detailRender);
        auto tip = MakeText(name + L"\n" + icon.GetNamedString(L"description", L"暂无说明"), 13);
        tip.TextWrapping(TextWrapping::Wrap);
        tip.MaxWidth(380);
        ToolTip tooltip;
        tooltip.Content(tip);
        ToolTipService::SetToolTip(tile, tooltip);
        Grid::SetRow(tile, i / 9);
        Grid::SetColumn(tile, i % 9);
        grid.Children().Append(tile);
    }
    return grid;
}
// Live refreshes keep the scroll position; switching tabs or heroes starts from the top.
void RenderDetails(bool resetScroll = true)
{
    ++detailRender;
    detailBody.Children().Clear();
    if (resetScroll)
        detailScroll.ChangeView(nullptr, 0.0, nullptr);
    for (size_t i = 0; i < tabButtons.size(); ++i)
    {
        tabButtons[i].Background(MakeBrush(DetailTabKeys[i] == detailTab ? 0x008b98 : 0x143542));
    }
    RenderRarityChoices();
    if (detailTab == L"cooldowns")
    {
        RenderCooldownCalculator();
        DecodeDetailImage(detailArtwork, detailData.GetNamedString(L"art", L""), detailRender, 560);
        return;
    }
    if (detailTab == L"augments")
    {
        if (!RenderAugmentResults())
            RenderMissingSection();
        DecodeDetailImage(detailArtwork, detailData.GetNamedString(L"art", L""), detailRender, 560);
        return;
    }
    bool found = false;
    auto sections = detailTab == L"live" ? liveSections : detailData.GetNamedArray(L"sections", JsonArray());
    for (auto const &entry : sections)
    {
        auto section = entry.GetObject();
        if (section.GetNamedString(L"tab", L"") != detailTab)
        {
            continue;
        }
        found = true;
        RenderDetailSection(section);
    }
    if (!found)
    {
        RenderMissingSection();
    }
    DecodeDetailImage(detailArtwork, detailData.GetNamedString(L"art", L""), detailRender, 560);
}
void RenderRarityChoices()
{
    if (detailTab == L"augments")
    {
        if (!augmentSearch)
        {
            augmentSearch = TextBox();
            augmentSearch.PlaceholderText(L"搜索海克斯：名称、效果或拼音首字母（如 tkyq），搜索时不分品质");
            augmentSearch.Margin({0, 0, 0, 6});
            UseChineseInput(augmentSearch);
            augmentSearch.TextChanged(
                [this](auto &&, auto &&)
                {
                    if (detailTab == L"augments")
                        RenderAugmentResults();
                });
        }
        detailBody.Children().Append(augmentSearch);
        StackPanel rarities;
        rarities.Orientation(Orientation::Horizontal);
        rarities.Margin({0, 0, 0, 6});
        std::array<hstring, 3> names{L"棱彩", L"黄金", L"白银"};
        for (int i = 0; i < 3; ++i)
        {
            auto b = DetailButton(names[i]);
            b.Margin({0, 0, 6, 0});
            b.Background(MakeBrush(i == detailRarity ? 0x3e375c : 0x143542));
            b.Click(
                [this, i](auto &&, auto &&)
                {
                    detailRarity = i;
                    RenderDetails();
                });
            rarities.Children().Append(b);
        }
        detailBody.Children().Append(rarities);
        detailBody.Children().Append(augmentResults);
    }
}
static constexpr std::array<wchar_t const *, 3> AugmentRarities{L"棱彩", L"黄金", L"白银"};
// 0 when the name or its initials match, 1 when only the description does, -1 for no match.
static int AugmentMatch(JsonObject const &row, std::wstring const &query)
{
    auto icons = row.GetNamedArray(L"icons", JsonArray());
    if (icons.Size() == 0)
        return -1;
    auto icon = icons.GetObjectAt(0);
    if (AugmentSearchText(icon.GetNamedString(L"name", L"") + L" " + row.GetNamedString(L"search", L"")).find(query) !=
        std::wstring::npos)
        return 0;
    return AugmentSearchText(icon.GetNamedString(L"description", L"")).find(query) != std::wstring::npos ? 1 : -1;
}
// Without a query the chosen rarity shows as before; with one, matches from every rarity, name matches first, each
// labelled with its rarity. Returns false when the source has no augment sections at all.
bool RenderAugmentResults()
{
    augmentResults.Children().Clear();
    std::vector<JsonObject> sections;
    for (auto const &entry : detailData.GetNamedArray(L"sections", JsonArray()))
        if (entry.GetObject().GetNamedString(L"tab", L"") == L"augments")
            sections.push_back(entry.GetObject());
    if (sections.empty())
        return false;
    auto query = AugmentSearchText(augmentSearch ? augmentSearch.Text() : hstring());
    query.erase(0, query.find_first_not_of(L' '));
    query.erase(query.find_last_not_of(L' ') + 1);
    if (query.empty())
    {
        auto const &section = sections[std::min<size_t>(detailRarity, sections.size() - 1)];
        RenderSectionHeading(section, augmentResults);
        std::vector<std::pair<JsonObject, hstring>> rows;
        for (auto const &value : section.GetNamedArray(L"rows", JsonArray()))
            rows.emplace_back(value.GetObject(), hstring());
        augmentResults.Children().Append(RecommendationTiles(rows));
        return true;
    }
    std::vector<std::pair<JsonObject, hstring>> names, descriptions;
    for (size_t i = 0; i < sections.size(); ++i)
        for (auto const &value : sections[i].GetNamedArray(L"rows", JsonArray()))
        {
            auto row = value.GetObject();
            int match = AugmentMatch(row, query);
            hstring rarity = i < AugmentRarities.size() ? hstring(AugmentRarities[i]) : hstring();
            if (match == 0)
                names.emplace_back(row, rarity);
            else if (match == 1)
                descriptions.emplace_back(row, rarity);
        }
    names.insert(names.end(), descriptions.begin(), descriptions.end());
    if (names.empty())
    {
        auto none = MakeText(L"没有匹配的海克斯。", 13, 0x91acbb);
        none.Margin({4, 6, 0, 6});
        augmentResults.Children().Append(none);
        return true;
    }
    auto count = MakeText(L"找到 " + to_hstring(names.size()) + L" 个（名称匹配在前）", 12, 0x91acbb);
    count.Margin({0, 0, 0, 6});
    augmentResults.Children().Append(count);
    augmentResults.Children().Append(RecommendationTiles(names));
    return true;
}
void RenderSectionHeading(JsonObject const &section, StackPanel const &target)
{
    auto title = MakeText(section.GetNamedString(L"title", L""), 15, 0xe0c688);
    title.Margin({0, 6, 0, 6});
    target.Children().Append(title);
    auto note = section.GetNamedString(L"note", L"");
    if (!note.empty())
    {
        auto text = MakeText(note, 12, 0x91acbb);
        text.TextWrapping(TextWrapping::Wrap);
        text.Margin({0, 0, 0, 8});
        target.Children().Append(text);
    }
}
void RenderDetailSection(JsonObject const &section)
{
    if (RenderCardSection(section))
        return;
    RenderSectionHeading(section, detailBody);
    auto rows = section.GetNamedArray(L"rows", JsonArray());
    if (rows.Size() == 0)
    {
        auto empty = MakeText(L"来源暂无这一项数据。", 13, 0x91acbb);
        empty.Margin({10, 6, 0, 10});
        detailBody.Children().Append(empty);
        return;
    }
    if (section.GetNamedString(L"layout", L"") == L"equipment")
    {
        std::vector<std::pair<JsonObject, hstring>> tiles;
        for (auto const &value : rows)
            tiles.emplace_back(value.GetObject(), hstring());
        detailBody.Children().Append(RecommendationTiles(tiles));
        return;
    }
    if (detailTab == L"skills" && rows.GetObjectAt(0).GetNamedArray(L"icons", JsonArray()).Size() > 0 &&
        rows.GetObjectAt(0).GetNamedArray(L"icons").GetObjectAt(0).GetNamedString(L"kind", L"") == L"skill")
    {
        RenderSkillChoices(rows);
        return;
    }
    for (auto const &value : rows)
    {
        detailBody.Children().Append(CreateDetailRow(value.GetObject()));
    }
}
// Three tiles a row; a label (the rarity in search results) goes before each name.
Grid RecommendationTiles(std::vector<std::pair<JsonObject, hstring>> const &rows)
{
    Grid tiles;
    for (int i = 0; i < 3; ++i)
    {
        tiles.ColumnDefinitions().Append(ColumnDefinition());
    }
    for (size_t i = 0; i < (rows.size() + 2) / 3; ++i)
    {
        RowDefinition row;
        row.Height({1, GridUnitType::Auto});
        tiles.RowDefinitions().Append(row);
    }
    for (size_t i = 0; i < rows.size(); ++i)
    {
        auto const &[row, label] = rows[i];
        auto icons = row.GetNamedArray(L"icons", JsonArray());
        if (icons.Size() == 0)
        {
            continue;
        }
        auto card = CreateRecommendationTile(row, icons, label);
        Grid::SetRow(card, static_cast<int32_t>(i / 3));
        Grid::SetColumn(card, static_cast<int32_t>(i % 3));
        tiles.Children().Append(card);
    }
    return tiles;
}
StackPanel CreateRecommendationTile(JsonObject const &row, JsonArray const &icons, hstring const &label)
{
    auto icon = icons.GetObjectAt(0);
    StackPanel card;
    card.Padding({8});
    card.Margin({0, 0, 6, 6});
    card.Background(MakeBrush(0x112f3c));
    auto name = MakeText((label.empty() ? hstring() : label + L" · ") + icon.GetNamedString(L"name", L"推荐"), 13);
    card.Children().Append(name);
    StackPanel metrics;
    metrics.Orientation(Orientation::Horizontal);
    metrics.Margin({0, 6, 0, 0});
    Image image;
    image.Width(40);
    image.Height(40);
    image.Margin({0, 0, 6, 0});
    DecodeDetailImage(image, icon.GetNamedString(L"image", L""), detailRender);
    metrics.Children().Append(image);
    StackPanel rates;
    rates.Children().Append(MakeText(row.GetNamedString(L"winRateText", L"胜率 —"), 12, 0x65d6d6));
    rates.Children().Append(MakeText(row.GetNamedString(L"pickRateText", L"选取率 —"), 11, 0xa4bcc7));
    metrics.Children().Append(rates);
    card.Children().Append(metrics);
    auto text = MakeText(icon.GetNamedString(L"name", L"") + L"\n" + icon.GetNamedString(L"description", L"暂无说明") +
                             L"\n" + row.GetNamedString(L"stats", L""),
                         13);
    text.TextWrapping(TextWrapping::Wrap);
    text.MaxWidth(380);
    ToolTip tip;
    tip.Content(text);
    ToolTipService::SetToolTip(card, tip);
    return card;
}
void RenderSkillChoices(JsonArray const &rows)
{
    StackPanel choices;
    choices.Orientation(Orientation::Horizontal);
    choices.Margin({0, 2, 0, 10});
    auto description = MakeText(L"", 14, 0xc0d5df);
    description.TextWrapping(TextWrapping::Wrap);
    description.Margin({12, 12, 12, 12});
    Border descriptionCard;
    descriptionCard.Background(MakeBrush(0x112f3c));
    descriptionCard.Child(description);
    for (uint32_t i = 0; i < rows.Size() && i < 5; ++i)
    {
        auto row = rows.GetObjectAt(i);
        if (row.GetNamedArray(L"icons", JsonArray()).Size() == 0)
        {
            continue;
        }
        choices.Children().Append(CreateSkillChoice(row, description, choices, i == 0));
    }
    if (choices.Children().Size() > 0)
        SelectSkillChoice(choices, choices.Children().GetAt(0).as<Button>());
    detailBody.Children().Append(choices);
    detailBody.Children().Append(descriptionCard);
}
// The description card shows one skill at a time; highlight which one it is.
static void SelectSkillChoice(StackPanel const &choices, Button const &selected)
{
    for (auto const &child : choices.Children())
    {
        auto button = child.as<Button>();
        bool active = button == selected;
        button.Background(MakeBrush(active ? 0x1f5a66 : 0x143542));
        button.BorderBrush(MakeBrush(active ? 0x7ff0ee : 0x315662));
        button.BorderThickness({active ? 2.0 : 1.0});
    }
}
Button CreateSkillChoice(JsonObject const &row, TextBlock const &description, StackPanel const &choices, bool initial)
{
    auto icon = row.GetNamedArray(L"icons").GetObjectAt(0);
    auto title = row.GetNamedString(L"title", L"");
    auto text = title + L"\n\n" + row.GetNamedString(L"description", L"暂无说明");
    StackPanel content;
    Image image;
    image.Width(44);
    image.Height(44);
    DecodeDetailImage(image, icon.GetNamedString(L"image", L""), detailRender);
    content.Children().Append(image);
    auto key = MakeText(icon.GetNamedString(L"key", L""), 13, 0xe0c688);
    key.HorizontalAlignment(HorizontalAlignment::Center);
    content.Children().Append(key);
    auto button = DetailButton(L"");
    button.Content(content);
    button.MinWidth(72);
    button.Margin({0, 0, 8, 0});
    // Weak reference: the panel owns its buttons, so a strong capture would form a cycle.
    button.Click(
        [description, text, weakChoices = make_weak(choices)](auto const &sender, auto &&)
        {
            description.Text(text);
            if (auto panel = weakChoices.get())
                SelectSkillChoice(panel, sender.template as<Button>());
        });
    auto tip = MakeText(text, 13);
    tip.TextWrapping(TextWrapping::Wrap);
    tip.MaxWidth(380);
    ToolTip tooltip;
    tooltip.Content(tip);
    ToolTipService::SetToolTip(button, tooltip);
    if (initial)
    {
        description.Text(text);
    }
    return button;
}
StackPanel CreateDetailRow(JsonObject const &row)
{
    StackPanel body;
    body.Padding({12, 8, 12, 8});
    body.Background(MakeBrush(0x112f3c));
    body.Margin({0, 0, 6, 6});
    auto icons = row.GetNamedArray(L"icons", JsonArray());
    auto name = row.GetNamedString(L"title", L"");
    if (name.empty() && icons.Size() > 0)
        name = icons.GetObjectAt(0).GetNamedString(L"name", L"");
    auto heading = MakeText(name, 14);
    heading.TextWrapping(TextWrapping::Wrap);
    body.Children().Append(heading);
    if (icons.Size() > 0)
        body.Children().Append(DetailIcons(icons));
    auto stats = row.GetNamedString(L"stats", L"");
    if (!stats.empty())
    {
        auto text = MakeText(stats, 12, 0x65d6d6);
        text.TextWrapping(TextWrapping::Wrap);
        body.Children().Append(text);
    }
    auto description = row.GetNamedString(L"description", L"");
    if (!description.empty())
    {
        auto text = MakeText(description, 13, 0xb4cad4);
        text.TextWrapping(TextWrapping::Wrap);
        text.Margin({0, 5, 0, 2});
        body.Children().Append(text);
    }
    auto later = row.GetNamedArray(L"later", JsonArray());
    if (later.Size() > 0)
    {
        auto button = DetailButton(L"后续可选装备 ▾");
        button.Margin({0, 6, 0, 0});
        auto choices = DetailIcons(later);
        choices.Visibility(Visibility::Collapsed);
        button.Click(
            [choices](auto &&, auto &&)
            {
                choices.Visibility(choices.Visibility() == Visibility::Visible ? Visibility::Collapsed
                                                                               : Visibility::Visible);
            });
        body.Children().Append(button);
        body.Children().Append(choices);
    }
    return body;
}
void RenderMissingSection()
{
    auto text = MakeText(detailTab == L"live"                                            ? L"等待本局数据…"
                         : detailData.GetNamedString(L"state", L"loading") == L"loading" ? L"正在读取这一页的数据…"
                                                                                         : L"当前模式暂无这一项数据。",
                         14,
                         0x91acbb);
    text.Margin({12, 20, 12, 20});
    text.TextWrapping(TextWrapping::Wrap);
    detailBody.Children().Append(text);
}
fire_and_forget ReadDetails()
{
    if (detailBusy || stopped || selectedCard < 0 || detailRequest.empty())
        co_return;
    if (!detailRevision.empty() && detailData.GetNamedString(L"state", L"loading") != L"loading")
        co_return;
    auto lifetime = shared_from_this();
    detailBusy = true;
    auto request = detailRequest;
    try
    {
        auto file = co_await ApplicationData::Current().LocalFolder().GetFileAsync(
            fixturePreview ? L"detail-preview-content.json" : L"detail.json");
        auto properties = co_await file.GetBasicPropertiesAsync();
        if (properties.Size() > 4000000)
            throw hresult_invalid_argument();
        auto contents = co_await FileIO::ReadTextAsync(file);
        if (!stopped && request == detailRequest)
        {
            auto data = JsonObject::Parse(contents);
            if (fixturePreview)
            {
                data.Insert(L"protocol", JsonValue::CreateNumberValue(1));
                data.Insert(L"session", JsonValue::CreateStringValue(session));
                data.Insert(L"requestId", JsonValue::CreateStringValue(request));
                data.Insert(L"championId", JsonValue::CreateNumberValue(cards[selectedCard].hero));
            }
            if (data.GetNamedNumber(L"protocol", 0) == 1 && data.GetNamedString(L"session", L"") == session &&
                data.GetNamedString(L"requestId", L"") == request && selectedCard >= 0 &&
                data.GetNamedNumber(L"championId", 0) == cards[selectedCard].hero)
            {
                auto version = data.GetNamedString(L"revision", L"");
                if (version != detailRevision)
                {
                    detailRevision = version;
                    detailData = data;
                    RenderDetails();
                    detailSource.Text(data.GetNamedString(L"mode", L"") + L" · " + data.GetNamedString(L"source", L""));
                }
            }
        }
    }
    catch (...)
    {
        // The main process may be replacing the reply atomically; retry the current selection on the next poll.
    }
    if (!stopped && request == detailRequest && Now() - detailRequestedAt > 50000 &&
        detailData.GetNamedString(L"state", L"loading") == L"loading")
        detailSource.Text(L"主程序尚未返回详情，请确认已启动新版 League.exe 后重试。");
    detailBusy = false;
}
