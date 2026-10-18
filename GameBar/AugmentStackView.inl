// Included inside WidgetView. The augment page: the one place augments are recorded, for every player, since 2999
// reports none. Each change is written to augment-stacks.json (augments with their anchor and choice) or
// ultimate-haste.json; the main program counts takedown augments from the game's events, sums every player's
// augments and applies them to health, haste, cooldowns and slow coverage. The other pages only show the result.
struct AugmentPick
{
    hstring player, augment;
    double time = 0;
    int stacks = 0;
    hstring choice;
};
static constexpr int MaxAugmentStacks = 999;
static constexpr double MaxUltimateHaste = 500;
Grid augmentRoot;
StackPanel augmentBody;
ScrollViewer augmentScroll;
Button augmentButton;
bool supportsAugments = false, augmentOpen = false, augmentsEdited = false, augmentsWriting = false;
bool ultimateEdited = false;
int augmentReturnCard = -1;
std::vector<AugmentPick> augmentPicks;
std::map<std::wstring, double> ultimateHaste;
JsonArray augmentStacks, augmentCatalog;
hstring pendingAugmentsWrite, pendingUltimateWrite;
// Snapshots arrive every few seconds in game; the page is redrawn only when what it shows changed, and never while
// the add list is open, because a redraw removes the button the list is attached to and closes it mid-typing.
hstring augmentSignature;
bool augmentPickerOpen = false, augmentRedrawPending = false;

// GetNamedString throws on a value of another type; an older or partial snapshot must not stop the roster.
static hstring AugmentText(JsonObject const &value, wchar_t const *name)
{
    auto entry = value.TryLookup(name);
    return entry && entry.ValueType() == JsonValueType::String ? entry.GetString() : hstring();
}
void InitializeAugmentPage()
{
    augmentButton = DetailButton(L"海克斯");
    augmentButton.Click([this](auto &&, auto &&) { OpenAugmentPage(); });
    ToolTipService::SetToolTip(augmentButton, box_value(L"记录双方英雄的海克斯，生命、急速和冷却按记录计算。"));
    augmentButton.Visibility(Visibility::Collapsed);
    for (int i = 0; i < 2; ++i)
    {
        RowDefinition r;
        r.Height({1, i == 1 ? GridUnitType::Star : GridUnitType::Auto});
        augmentRoot.RowDefinitions().Append(r);
    }
    StackPanel navigation;
    navigation.Orientation(Orientation::Horizontal);
    navigation.Margin({0, 0, 0, 8});
    auto back = DetailButton(L"‹ 返回");
    back.Margin({0, 0, 10, 0});
    back.Click([this](auto &&, auto &&) { CloseAugmentPage(); });
    navigation.Children().Append(back);
    auto title = MakeText(L"海克斯", 20, 0xe9d8a6);
    title.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(title);
    auto info = MakeText(L"ⓘ", 14, 0x91acbb);
    info.Margin({8, 2, 0, 0});
    info.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(info);
    SetRecordTip(navigation,
                 L"2999 不提供海克斯，在这里手动记录；生命、技能急速、冷却和减速覆盖率都按这里的记录计算。\n"
                 L"自动计层：参与击杀和死亡从本局击杀事件推算，− / + 修正后从当前时间重新计数。\n"
                 L"手动层数：命中类的叠层读不到，自己按 − / + 记录。\n"
                 L"固定加成：添加即生效。\n大招急速：符文等来源 2999 不提供，手动输入，只作用于大招。");
    augmentRoot.Children().Append(navigation);
    augmentScroll.Content(augmentBody);
    augmentScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
    augmentScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
    Grid::SetRow(augmentScroll, 1);
    augmentRoot.Children().Append(augmentScroll);
    Grid::SetRow(augmentRoot, 2);
    Grid::SetRowSpan(augmentRoot, 2);
    augmentRoot.Visibility(Visibility::Collapsed);
    root.Children().Append(augmentRoot);
}
void ClearAugments()
{
    CloseAugmentPage();
    augmentPicks.clear();
    augmentsEdited = false;
    ultimateHaste.clear();
    ultimateEdited = false;
    augmentStacks = JsonArray();
}
// The snapshot echoes each record, so a restarted widget takes the lists back until the user changes them here.
void ApplyAugments(JsonObject const &data)
{
    supportsAugments = data.GetNamedNumber(L"augmentProtocol", 0) == 2;
    augmentStacks = data.GetNamedArray(L"augmentStacks", JsonArray());
    augmentCatalog = data.GetNamedArray(L"augmentCatalog", JsonArray());
    if (!augmentsEdited)
    {
        augmentPicks.clear();
        for (auto const &entry : augmentStacks)
        {
            auto stack = entry.GetObject();
            augmentPicks.push_back({AugmentText(stack, L"player"),
                                    AugmentText(stack, L"augment"),
                                    stack.GetNamedNumber(L"time", 0),
                                    static_cast<int>(stack.GetNamedNumber(L"start", 0)),
                                    AugmentText(stack, L"choice")});
        }
    }
    if (!ultimateEdited)
    {
        ultimateHaste.clear();
        auto value = data.TryLookup(L"ultimateHaste");
        if (value && value.ValueType() == JsonValueType::Object)
            for (auto const &entry : value.GetObject())
                if (entry.Value().ValueType() == JsonValueType::Number)
                    ultimateHaste[std::wstring(entry.Key())] = entry.Value().GetNumber();
    }
    augmentButton.Visibility(supportsAugments ? Visibility::Visible : Visibility::Collapsed);
}
JsonObject CatalogEntry(hstring const &augment)
{
    for (auto const &entry : augmentCatalog)
        if (AugmentText(entry.GetObject(), L"augment") == augment)
            return entry.GetObject();
    return nullptr;
}
AugmentPick *FindAugmentPick(hstring const &player, hstring const &augment)
{
    for (auto &pick : augmentPicks)
        if (pick.player == player && pick.augment == augment)
            return &pick;
    return nullptr;
}
// The counted state for a pick, once the main program has seen its current record.
JsonObject AugmentState(AugmentPick const &pick)
{
    for (auto const &entry : augmentStacks)
    {
        auto stack = entry.GetObject();
        if (AugmentText(stack, L"player") == pick.player && AugmentText(stack, L"augment") == pick.augment &&
            std::abs(stack.GetNamedNumber(L"time", -1) - pick.time) < 0.01 &&
            static_cast<int>(stack.GetNamedNumber(L"start", -1)) == pick.stacks)
            return stack;
    }
    return nullptr;
}
static int AugmentShownStacks(AugmentPick const &pick, JsonObject const &state)
{
    return state ? static_cast<int>(state.GetNamedNumber(L"stacks", 0)) : pick.stacks;
}
// "名称 · 加成" as the main program words it, or the record while it catches up.
hstring AugmentChipText(AugmentPick const &pick, JsonObject const &state)
{
    auto entry = CatalogEntry(pick.augment);
    hstring name = entry ? AugmentText(entry, L"name") : pick.augment;
    if (!state)
        return name + L" · 更新中…";
    hstring prefix = state.GetNamedBoolean(L"approximate", false) ? L"约 " : L"";
    return name + L" · " + prefix + AugmentText(state, L"bonus");
}
// Takedown augments anchor at the game time; the others need no time, and corrections there are plain counts.
void SetAugmentRecord(hstring const &player, hstring const &augment, int stacks, hstring const &choice)
{
    auto entry = CatalogEntry(augment);
    bool takedowns = entry && AugmentText(entry, L"kind") == L"Takedowns";
    double now = takedowns ? WaveGameTime() : 0;
    if (now < 0 || player.empty())
        return;
    stacks = (std::clamp)(stacks, 0, MaxAugmentStacks);
    if (auto pick = FindAugmentPick(player, augment))
        *pick = {player, augment, now, stacks, choice};
    else
        augmentPicks.push_back({player, augment, now, stacks, choice});
    SaveAugments();
}
void RemoveAugment(hstring const &player, hstring const &augment)
{
    augmentPicks.erase(std::remove_if(augmentPicks.begin(),
                                      augmentPicks.end(),
                                      [&](AugmentPick const &pick)
                                      { return pick.player == player && pick.augment == augment; }),
                       augmentPicks.end());
    SaveAugments();
}
void SaveAugments()
{
    augmentsEdited = true;
    if (augmentOpen)
        RenderAugmentPage();
    RenderStatsAfterEdit();
    if (!widget || fixturePreview || stopped)
        return;
    JsonArray augments;
    for (auto const &pick : augmentPicks)
    {
        JsonObject value;
        value.Insert(L"player", JsonValue::CreateStringValue(pick.player));
        value.Insert(L"augment", JsonValue::CreateStringValue(pick.augment));
        value.Insert(L"time", JsonValue::CreateNumberValue(pick.time));
        value.Insert(L"stacks", JsonValue::CreateNumberValue(pick.stacks));
        value.Insert(L"choice", JsonValue::CreateStringValue(pick.choice));
        augments.Append(value);
    }
    JsonObject file;
    file.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    file.Insert(L"session", JsonValue::CreateStringValue(session));
    file.Insert(L"augments", augments);
    pendingAugmentsWrite = file.Stringify();
    WriteAugmentFiles();
}
void SetUltimateHaste(hstring const &player, double value)
{
    ultimateHaste[std::wstring(player)] = (std::clamp)(value, 0.0, MaxUltimateHaste);
    ultimateEdited = true;
    if (augmentOpen)
        RenderAugmentPage();
    RenderStatsAfterEdit();
    if (!widget || fixturePreview || stopped)
        return;
    JsonArray players;
    for (auto const &[key, haste] : ultimateHaste)
    {
        JsonObject value;
        value.Insert(L"player", JsonValue::CreateStringValue(key));
        value.Insert(L"ultimateHaste", JsonValue::CreateNumberValue(haste));
        players.Append(value);
    }
    JsonObject file;
    file.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    file.Insert(L"session", JsonValue::CreateStringValue(session));
    file.Insert(L"players", players);
    pendingUltimateWrite = file.Stringify();
    WriteAugmentFiles();
}
// Both files go through one writer, each replaced atomically, so the main program never reads half a file.
fire_and_forget WriteAugmentFiles()
{
    if (augmentsWriting || stopped)
        co_return;
    auto lifetime = shared_from_this();
    augmentsWriting = true;
    try
    {
        while ((!pendingAugmentsWrite.empty() || !pendingUltimateWrite.empty()) && !stopped)
        {
            bool augments = !pendingAugmentsWrite.empty();
            auto value = augments ? pendingAugmentsWrite : pendingUltimateWrite;
            if (augments)
                pendingAugmentsWrite = L"";
            else
                pendingUltimateWrite = L"";
            hstring name = augments ? L"augment-stacks" : L"ultimate-haste";
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                name + L".tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, value);
            co_await file.RenameAsync(name + L".json", NameCollisionOption::ReplaceExisting);
        }
    }
    catch (hresult_error const &e)
    {
        // The page keeps the records; the main program keeps the last ones it read until the next change.
        Trace(L"augment-write-error " + e.message());
    }
    augmentsWriting = false;
}
Button AugmentButton(hstring const &text, std::function<void()> action, bool enabled = true)
{
    auto button = DetailButton(text);
    button.Padding({6, 0, 6, 0});
    button.Margin({2, 0, 0, 0});
    button.IsEnabled(enabled);
    button.Click([action](auto &&, auto &&) { action(); });
    return button;
}
hstring ChoiceLabel(JsonObject const &entry, hstring const &choice)
{
    for (auto const &value : entry.GetNamedArray(L"choices", JsonArray()))
        if (AugmentText(value.GetObject(), L"key") == choice)
            return AugmentText(value.GetObject(), L"label");
    return choice;
}
// The choice an augment needs, moving to the next option on each click.
void AppendAugmentChoice(StackPanel const &group, AugmentPick const &pick, JsonObject const &entry)
{
    auto choices = entry.GetNamedArray(L"choices", JsonArray());
    if (choices.Size() == 0)
        return;
    hstring current = pick.choice.empty() ? AugmentText(choices.GetObjectAt(0), L"key") : pick.choice;
    uint32_t next = 0;
    for (uint32_t i = 0; i < choices.Size(); ++i)
        if (AugmentText(choices.GetObjectAt(i), L"key") == current)
            next = (i + 1) % choices.Size();
    auto nextKey = AugmentText(choices.GetObjectAt(next), L"key");
    auto player = pick.player;
    auto augment = pick.augment;
    int stacks = pick.stacks;
    double time = pick.time;
    group.Children().Append(AugmentButton(ChoiceLabel(entry, current) + L" ⇄",
                                          [this, player, augment, stacks, time, nextKey]
                                          {
                                              if (auto found = FindAugmentPick(player, augment))
                                              {
                                                  *found = {player, augment, time, stacks, nextKey};
                                                  SaveAugments();
                                              }
                                          }));
}
UIElement AugmentPickControls(AugmentPick const &pick, bool timed)
{
    auto entry = CatalogEntry(pick.augment);
    auto kind = entry ? AugmentText(entry, L"kind") : hstring();
    auto state = AugmentState(pick);
    StackPanel group;
    group.Orientation(Orientation::Horizontal);
    group.Margin({0, 0, 8, 4});
    bool incomplete = state && !state.GetNamedBoolean(L"complete", true);
    group.Children().Append(ControlChip(AugmentChipText(pick, state),
                                        incomplete ? 0xe0b25c : 0xb8a2ff,
                                        (entry ? AugmentText(entry, L"effect") : hstring()) +
                                            (state ? L"\n" + AugmentText(state, L"note") : hstring())));
    if (entry)
        AppendAugmentChoice(group, pick, entry);
    int stacks = AugmentShownStacks(pick, state);
    auto player = pick.player;
    auto augment = pick.augment;
    auto choice = pick.choice;
    if (kind == L"Takedowns" || kind == L"Manual")
    {
        bool allowed = kind != L"Takedowns" || timed;
        int top = augment == L"PursuitOfHaste" ? 2 : MaxAugmentStacks;
        for (int step : {-1, 1})
            group.Children().Append(AugmentButton(
                step < 0 ? L"−" : L"+",
                [this, player, augment, stacks, step, choice]
                { SetAugmentRecord(player, augment, stacks + step, choice); },
                allowed && stacks + step >= 0 && stacks + step <= top));
        if (augment == L"StayResolute")
            group.Children().Append(AugmentButton(L"+5",
                                                  [this, player, augment, stacks, choice]
                                                  { SetAugmentRecord(player, augment, stacks + 5, choice); }));
    }
    group.Children().Append(AugmentButton(L"×", [this, player, augment] { RemoveAugment(player, augment); }));
    return group;
}
// Lower case, with full-width letters and digits (what a Chinese input method may type) folded to plain ones.
static std::wstring AugmentSearchText(hstring const &text)
{
    std::wstring folded(text);
    for (auto &c : folded)
    {
        if (c >= 0xFF01 && c <= 0xFF5E)
            c = static_cast<wchar_t>(c - 0xFEE0);
        c = static_cast<wchar_t>(std::towlower(c));
    }
    return folded;
}
static hstring AugmentKindLabel(hstring const &kind)
{
    return kind == L"Takedowns" ? L"自动计层" : kind == L"Manual" ? L"手动层数" : L"固定加成";
}
// The Game Bar overlay may keep the input method in English; this asks for Chinese input, and the search also
// accepts pinyin and initials for when it does not.
static void UseChineseInput(TextBox const &box)
{
    Windows::UI::Xaml::Input::InputScope scope;
    Windows::UI::Xaml::Input::InputScopeName name;
    name.NameValue(Windows::UI::Xaml::Input::InputScopeNameValue::ChineseHalfWidth);
    scope.Names().Append(name);
    box.InputScope(scope);
}
// One entry of the add list: name and kind on the first line, the effect under it.
Button AugmentChoiceItem(JsonObject const &entry, bool enabled)
{
    StackPanel content;
    StackPanel title;
    title.Orientation(Orientation::Horizontal);
    title.Children().Append(MakeText(AugmentText(entry, L"name"), 13, 0xe2eef3));
    auto kind = MakeText(AugmentKindLabel(AugmentText(entry, L"kind")), 11, 0x91acbb);
    kind.Margin({8, 2, 0, 0});
    title.Children().Append(kind);
    content.Children().Append(title);
    auto effect = MakeText(enabled ? AugmentText(entry, L"effect") : hstring(L"需要对局时间后才能添加"), 11, 0x91acbb);
    effect.TextWrapping(TextWrapping::Wrap);
    content.Children().Append(effect);
    Button item;
    item.Content(content);
    item.HorizontalAlignment(HorizontalAlignment::Stretch);
    item.HorizontalContentAlignment(HorizontalAlignment::Left);
    item.Background(MakeBrush(0x143542));
    item.BorderThickness({0, 0, 0, 0});
    item.Padding({8, 4, 8, 4});
    item.Margin({0, 0, 0, 2});
    item.IsEnabled(enabled);
    return item;
}
// The add list's state: the typed text, the chosen category and every entry with what it matches.
struct AugmentFilter
{
    struct Entry
    {
        Button item{nullptr};
        std::wstring text;
        std::vector<hstring> tags;
    };
    std::wstring query;
    hstring tag;
    std::vector<Entry> entries;
    std::vector<std::pair<Button, hstring>> chips;
    TextBlock none{nullptr};

    void Apply() const
    {
        bool any = false;
        for (auto const &entry : entries)
        {
            bool tagged = tag.empty() || std::find(entry.tags.begin(), entry.tags.end(), tag) != entry.tags.end();
            bool match = tagged && (query.empty() || entry.text.find(query) != std::wstring::npos);
            entry.item.Visibility(match ? Visibility::Visible : Visibility::Collapsed);
            any = any || match;
        }
        none.Visibility(any ? Visibility::Collapsed : Visibility::Visible);
        for (auto const &[chip, key] : chips)
            chip.Background(MakeBrush(key == tag ? 0x3e375c : 0x143542));
    }
};
// Category buttons from the catalog's tags, in the order they first appear, plus the takedown kind.
StackPanel AugmentCategoryChips(std::shared_ptr<AugmentFilter> const &filter)
{
    std::vector<hstring> tags{L""};
    for (auto const &value : augmentCatalog)
        for (auto const &tag : value.GetObject().GetNamedArray(L"tags", JsonArray()))
            if (tag.ValueType() == JsonValueType::String &&
                std::find(tags.begin(), tags.end(), tag.GetString()) == tags.end())
                tags.push_back(tag.GetString());
    tags.push_back(L"自动计层");
    StackPanel row;
    row.Orientation(Orientation::Horizontal);
    row.Margin({0, 0, 0, 6});
    for (auto const &tag : tags)
    {
        auto chip = AugmentButton(tag.empty() ? hstring(L"全部") : tag,
                                  [weak = std::weak_ptr<AugmentFilter>(filter), tag]
                                  {
                                      if (auto state = weak.lock())
                                      {
                                          state->tag = tag;
                                          state->Apply();
                                      }
                                  });
        chip.Padding({8, 2, 8, 2});
        filter->chips.emplace_back(chip, tag);
        row.Children().Append(chip);
    }
    return row;
}
// The add button opens a searchable list of every catalogued augment the player does not have yet. The search
// matches the name, effect, English key, pinyin and initials, ignoring case and full-width letters.
Button AugmentAddButton(hstring const &player, bool timed)
{
    Flyout flyout;
    auto filter = std::make_shared<AugmentFilter>();
    StackPanel panel;
    panel.Width(360);
    TextBox search;
    search.PlaceholderText(L"搜索名称、效果或拼音（如 tk）");
    search.Margin({0, 0, 0, 6});
    UseChineseInput(search);
    panel.Children().Append(search);
    panel.Children().Append(AugmentCategoryChips(filter));
    StackPanel list;
    for (auto const &value : augmentCatalog)
    {
        auto entry = value.GetObject();
        auto augment = AugmentText(entry, L"augment");
        if (FindAugmentPick(player, augment))
            continue;
        bool takedowns = AugmentText(entry, L"kind") == L"Takedowns";
        auto item = AugmentChoiceItem(entry, !takedowns || timed);
        item.Click(
            [this, weak = make_weak(flyout), player, augment](auto &&, auto &&)
            {
                // The page is rebuilt by the change; close the list first so it is not left over a removed button.
                if (auto open = weak.get())
                    open.Hide();
                SetAugmentRecord(player, augment, 0, L"");
            });
        list.Children().Append(item);
        std::vector<hstring> tags;
        for (auto const &tag : entry.GetNamedArray(L"tags", JsonArray()))
            if (tag.ValueType() == JsonValueType::String)
                tags.push_back(tag.GetString());
        if (takedowns)
            tags.push_back(L"自动计层");
        filter->entries.push_back(
            {item,
             AugmentSearchText(AugmentText(entry, L"name") + L" " + AugmentText(entry, L"effect") + L" " + augment +
                               L" " + AugmentText(entry, L"pinyin")),
             tags});
    }
    filter->none = MakeText(L"没有匹配的海克斯", 12, 0x6f8793);
    filter->none.Margin({4, 4, 0, 4});
    list.Children().Append(filter->none);
    filter->Apply();
    search.TextChanged(
        [weak = make_weak(search), filter](auto &&, auto &&)
        {
            if (auto box = weak.get())
            {
                filter->query = AugmentSearchText(box.Text());
                filter->Apply();
            }
        });
    ScrollViewer scroll;
    scroll.MaxHeight(360);
    scroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
    scroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
    scroll.Content(list);
    panel.Children().Append(scroll);
    flyout.Content(panel);
    flyout.Opened(
        [this, search](auto &&, auto &&)
        {
            augmentPickerOpen = true;
            search.Focus(FocusState::Programmatic);
        });
    flyout.Closed(
        [this](auto &&, auto &&)
        {
            augmentPickerOpen = false;
            if (augmentRedrawPending)
            {
                augmentRedrawPending = false;
                RefreshAugmentPage();
                RefreshStatsPage();
            }
        });
    auto add = AugmentButton(L"＋ 海克斯", [] {}, !filter->entries.empty());
    add.Padding({8, 2, 8, 2});
    add.Margin({0, 0, 0, 4});
    add.Flyout(flyout);
    return add;
}
StackPanel UltimateHasteLine(hstring const &player)
{
    StackPanel line;
    line.Orientation(Orientation::Horizontal);
    line.Margin({0, 2, 0, 0});
    double value = 0;
    if (auto found = ultimateHaste.find(std::wstring(player)); found != ultimateHaste.end())
        value = found->second;
    auto label = MakeText(L"大招急速 " + to_hstring(static_cast<int>(std::lround(value))), 12, 0xa4bcc7);
    label.VerticalAlignment(VerticalAlignment::Center);
    label.Margin({0, 0, 4, 0});
    SetRecordTip(label, L"符文（如终极猎人）等来源的大招急速，2999 不提供；终极唤醒的 +30 会自动计入，不用在这里加。");
    line.Children().Append(label);
    for (int step : {-10, -1, 1, 10})
    {
        hstring text = step > 0 ? L"+" + to_hstring(step) : L"−" + to_hstring(-step);
        line.Children().Append(AugmentButton(
            text, [this, player, value, step] { SetUltimateHaste(player, value + step); }, step > 0 || value > 0));
    }
    return line;
}
Border CreateAugmentRow(uint32_t slot, JsonObject const &row, uint32_t accent, bool timed)
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
    frame.VerticalAlignment(VerticalAlignment::Top);
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
    header.Margin({0, 0, 0, 4});
    header.Children().Append(MakeText(row.GetNamedString(L"champion", L""), 14));
    auto player = MakeText(row.GetNamedString(L"name", L""), 11, 0x91acbb);
    player.Margin({8, 2, 0, 0});
    header.Children().Append(player);
    text.Children().Append(header);
    auto key = cards[slot].key;
    // One augment per line: their controls differ in width, and a wrap grid would cut the wider ones.
    StackPanel picks;
    for (auto const &pick : augmentPicks)
        if (pick.player == key)
            picks.Children().Append(AugmentPickControls(pick, timed));
    auto add = AugmentAddButton(key, timed);
    add.HorizontalAlignment(HorizontalAlignment::Left);
    picks.Children().Append(add);
    text.Children().Append(picks);
    text.Children().Append(UltimateHasteLine(key));
    layout.Children().Append(text);
    return RecordCard(layout, accent);
}
void OpenAugmentPage()
{
    if (!supportsAugments || populatedCards == 0)
        return;
    CloseWaves();
    CloseControls();
    CloseComposition();
    augmentReturnCard = selectedCard;
    if (selectedCard >= 0)
        detailRoot.Visibility(Visibility::Collapsed);
    rosterPanel.Visibility(Visibility::Collapsed);
    details.Visibility(Visibility::Collapsed);
    augmentRoot.Visibility(Visibility::Visible);
    augmentOpen = true;
    augmentScroll.ChangeView(nullptr, 0.0, nullptr);
    RenderAugmentPage();
}
void CloseAugmentPage()
{
    if (!augmentOpen)
        return;
    augmentOpen = false;
    augmentBody.Children().Clear();
    augmentRoot.Visibility(Visibility::Collapsed);
    if (augmentReturnCard >= 0 && augmentReturnCard == selectedCard)
    {
        detailRoot.Visibility(Visibility::Visible);
        RefreshStatsPage();
    }
    else
    {
        rosterPanel.Visibility(Visibility::Visible);
        UpdateRosterGuide();
    }
    augmentReturnCard = -1;
}
// Everything the page shows that a snapshot can change: the counted records, the roster and its icons, the
// ultimate haste and whether takedown augments can be anchored.
hstring AugmentPageSignature()
{
    std::wstring text(augmentStacks.Stringify());
    text += WaveGameTime() >= 0 ? L"|timed|" : L"|untimed|";
    for (uint32_t slot = 0; slot < controlRows.Size() && slot < cards.size(); ++slot)
    {
        auto row = controlRows.GetObjectAt(slot);
        text += std::wstring(cards[slot].key) + L"," + std::to_wstring(cards[slot].hero) + L"," +
                std::wstring(row.GetNamedString(L"name", L"")) + (cards[slot].icon.Source() ? L",i;" : L",-;");
    }
    for (auto const &[key, haste] : ultimateHaste)
        text += key + L"=" + std::to_wstring(haste) + L";";
    text += std::to_wstring(augmentCatalog.Size());
    return hstring(text);
}
// Called for snapshots and arriving icons; the user's own changes redraw at once through RenderAugmentPage.
void RefreshAugmentPage()
{
    if (!augmentOpen || AugmentPageSignature() == augmentSignature)
        return;
    if (augmentPickerOpen)
    {
        augmentRedrawPending = true;
        return;
    }
    RenderAugmentPage();
}
// Enemies first, in roster order, as on the control page; the snapshot lists the own team in slots 0-4.
void RenderAugmentPage()
{
    augmentSignature = AugmentPageSignature();
    augmentBody.Children().Clear();
    bool timed = WaveGameTime() >= 0;
    if (!timed)
    {
        auto wait = MakeText(L"等待对局时间：自动计层的海克斯（参与击杀叠层）暂时不能添加或修正。", 12, 0xe0b25c);
        wait.TextWrapping(TextWrapping::Wrap);
        wait.Margin({0, 0, 0, 6});
        augmentBody.Children().Append(wait);
    }
    for (int team : {1, 0})
    {
        auto heading = MakeText(teams[team].Text(), 15, team == 1 ? 0xf3b37e : 0x45c7ce);
        heading.Margin({0, team == 1 ? 0.0 : 8.0, 0, 6});
        augmentBody.Children().Append(heading);
        for (uint32_t slot = team * 5; slot < team * 5u + 5 && slot < controlRows.Size(); ++slot)
        {
            auto row = controlRows.GetObjectAt(slot);
            if (row.GetNamedNumber(L"championId", 0) > 0)
                augmentBody.Children().Append(CreateAugmentRow(slot, row, team == 1 ? 0xf3b37e : 0x45c7ce, timed));
        }
    }
}
// The control page shows a player's records read-only; changes are made on the augment or stat page.
std::vector<UIElement> AugmentSummaryChips(hstring const &player)
{
    std::vector<UIElement> chips;
    for (auto const &pick : augmentPicks)
        if (pick.player == player)
        {
            auto entry = CatalogEntry(pick.augment);
            chips.push_back(ControlChip(AugmentChipText(pick, AugmentState(pick)),
                                        0xb8a2ff,
                                        (entry ? AugmentText(entry, L"effect") : hstring()) +
                                            L"\n在「海克斯」页或英雄的「属性」页修改。"));
        }
    if (chips.empty())
        chips.push_back(MakeText(L"未记录（在「海克斯」页或英雄的「属性」页添加）", 12, 0x6f8793));
    return chips;
}
