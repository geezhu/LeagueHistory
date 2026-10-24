// Included inside WidgetView. The main program computes wave times from the rules; this page only counts down.
Grid waveRoot;
StackPanel waveBody;
ScrollViewer waveScroll;
Button waveButton;
TextBlock waveTitle = MakeText(L"兵线", 20, 0xe9d8a6);
TextBlock waveRules = MakeText(L"", 12, 0xdbc47f);
JsonObject waves;
bool waveOpen = false;
int waveReturnCard = -1;
int64_t waveRenderedSecond = -1;
// The chosen stop per lane as a fraction of the bar from the own nexus; negative means the default stop.
std::array<double, 3> waveStops{-1, -1, -1};
bool waveRenderPending = false;
// Moves the wave dots every frame while the page is open; the page itself is rebuilt once a second.
CompositionTarget::Rendering_revoker waveFrames;
// The cannon-wave sound, played by the main program: off, as the wave before a cannon wave spawns, or as the cannon
// wave spawns. Stored in the widget and sent with the control settings.
Button cannonSoundButton;
std::array<ToggleMenuFlyoutItem, 3> cannonSoundItems;
hstring cannonSound = L"off";
static constexpr wchar_t const *CannonSoundSetting = L"cannonWaveSound";
static constexpr std::array<wchar_t const *, 3> CannonSoundKeys{L"off", L"next", L"spawn"};
static constexpr std::array<wchar_t const *, 3> CannonSoundNames{L"关", L"下一波是炮车时", L"炮车波出兵时"};

void InitializeWaves()
{
    waveButton = DetailButton(L"兵线");
    waveButton.Click([this](auto &&, auto &&) { OpenWaves(); });
    for (int i = 0; i < 2; ++i)
    {
        RowDefinition r;
        r.Height({1, i == 1 ? GridUnitType::Star : GridUnitType::Auto});
        waveRoot.RowDefinitions().Append(r);
    }
    StackPanel navigation;
    navigation.Orientation(Orientation::Horizontal);
    navigation.Margin({0, 0, 0, 8});
    auto back = DetailButton(L"‹ 返回");
    back.Margin({0, 0, 10, 0});
    back.Click([this](auto &&, auto &&) { CloseWaves(); });
    navigation.Children().Append(back);
    waveTitle.VerticalAlignment(VerticalAlignment::Center);
    navigation.Children().Append(waveTitle);
    // Current and upcoming wave rules sit beside the title instead of taking lines of their own.
    waveRules.VerticalAlignment(VerticalAlignment::Center);
    waveRules.Margin({16, 2, 0, 0});
    navigation.Children().Append(waveRules);
    InitializeCannonSound(navigation);
    waveRoot.Children().Append(navigation);
    waveScroll.Content(waveBody);
    waveScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
    waveScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
    Grid::SetRow(waveScroll, 1);
    waveRoot.Children().Append(waveScroll);
    Grid::SetRow(waveRoot, 2);
    Grid::SetRowSpan(waveRoot, 2);
    waveRoot.Visibility(Visibility::Collapsed);
    root.Children().Append(waveRoot);
}
void InitializeCannonSound(StackPanel const &navigation)
{
    auto stored = ApplicationData::Current().LocalSettings().Values().TryLookup(CannonSoundSetting);
    auto value = stored ? unbox_value_or<hstring>(stored, L"off") : hstring(L"off");
    cannonSound =
        std::find(CannonSoundKeys.begin(), CannonSoundKeys.end(), std::wstring_view(value)) != CannonSoundKeys.end()
            ? value
            : hstring(L"off");
    MenuFlyout menu;
    for (size_t i = 0; i < cannonSoundItems.size(); ++i)
    {
        cannonSoundItems[i].Text(CannonSoundNames[i]);
        cannonSoundItems[i].Click(
            [this, key = hstring(CannonSoundKeys[i])](auto &&, auto &&)
            {
                cannonSound = key;
                ApplicationData::Current().LocalSettings().Values().Insert(CannonSoundSetting, box_value(key));
                RefreshCannonSoundButton();
                SaveControlSettings();
            });
        menu.Items().Append(cannonSoundItems[i]);
    }
    cannonSoundButton = DetailButton(L"");
    cannonSoundButton.Margin({16, 0, 0, 0});
    cannonSoundButton.Flyout(menu);
    ToolTipService::SetToolTip(
        cannonSoundButton,
        box_value(L"由 League 主程序播放两声低音：「下一波是炮车时」在炮车波的前一波出兵时提示，约提前一波；"
                  L"「炮车波出兵时」在炮车波出兵时提示。选择时不试播。"));
    RefreshCannonSoundButton();
    navigation.Children().Append(cannonSoundButton);
}
void RefreshCannonSoundButton()
{
    hstring state = L"关";
    for (size_t i = 0; i < cannonSoundItems.size(); ++i)
    {
        bool chosen = cannonSound == CannonSoundKeys[i];
        cannonSoundItems[i].IsChecked(chosen);
        if (chosen && i > 0)
            state = i == 1 ? L"下一波" : L"出兵时";
    }
    cannonSoundButton.Content(MakeText(L"炮车提示音：" + state + L" ▾", 13));
}
bool HasWaves() const
{
    return WaveDataReady(waves);
}
double WaveGameTime()
{
    return WaveGameNow(waves, fixturePreview);
}
void ApplyWaves(JsonObject const &data)
{
    auto value = data.TryLookup(L"waves");
    waves = value && value.ValueType() == JsonValueType::Object ? value.GetObject() : JsonObject();
    waveRenderedSecond = -1;
    UpdateWaves();
}
void UpdateWaves()
{
    if (stopped)
        return;
    double now = WaveGameTime();
    JsonObject next{nullptr};
    if (HasWaves() && now >= 0)
        next = NextSpawn(waves, now);
    waveButton.IsEnabled(HasWaves());
    hstring label = L"兵线";
    // A cannon wave counting down lights the button, so it can be seen without opening the page.
    bool cannon = next && next.GetNamedNumber(L"siege", 0) > 0;
    if (next)
        label = label + (cannon ? L" 炮车 " : L" ") + WaveCountdown(next.GetNamedNumber(L"time", 0) - now);
    waveButton.Content(MakeText(label, 13, cannon ? 0x1a1206 : 0xe2eef3));
    waveButton.Background(MakeBrush(cannon ? 0xe0b25c : 0x143542));
    waveButton.BorderBrush(MakeBrush(cannon ? 0xf3d48a : 0x315662));
    hstring tip = HasWaves() ? hstring(L"按游戏时间推算出波、炮车、超级兵和兵线到达防御塔的时间。")
                  : active   ? hstring(L"当前模式没有兵线。")
                             : hstring(L"进入对局后可用。");
    ToolTipService::SetToolTip(waveButton, box_value(tip));
    if (waveOpen)
    {
        int64_t second = now < 0 ? -2 : static_cast<int64_t>(now);
        if (second != waveRenderedSecond)
        {
            waveRenderedSecond = second;
            RenderWaves(now);
        }
    }
}
// A tap must not rebuild the bar while its Tapped event is still routing: clearing the page there destroys the
// sender and crashed the widget on repeated taps. The rebuild runs after the event, once per burst of taps.
void RequestWaveRender()
{
    waveRenderedSecond = -1;
    if (waveRenderPending)
        return;
    waveRenderPending = true;
    root.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Low,
                               [weak = weak_from_this()]
                               {
                                   if (auto self = weak.lock())
                                   {
                                       self->waveRenderPending = false;
                                       self->UpdateWaves();
                                   }
                               });
}
void OpenWaves()
{
    if (!HasWaves())
        return;
    CloseControls();
    CloseComposition();
    waveReturnCard = selectedCard;
    if (selectedCard >= 0)
        detailRoot.Visibility(Visibility::Collapsed);
    rosterPanel.Visibility(Visibility::Collapsed);
    details.Visibility(Visibility::Collapsed);
    waveRoot.Visibility(Visibility::Visible);
    waveOpen = true;
    waveRenderedSecond = -1;
    waveScroll.ChangeView(nullptr, 0.0, nullptr);
    UpdateWaves();
    waveFrames = CompositionTarget::Rendering(auto_revoke,
                                              [weak = weak_from_this()](auto &&, auto &&)
                                              {
                                                  if (auto self = weak.lock(); self && !self->stopped)
                                                      ::MoveWaveDots(self->waveDots, self->WaveGameTime());
                                              });
}
// Back returns to the roster or the hero that was open, without re-requesting its details.
void CloseWaves()
{
    if (!waveOpen)
        return;
    waveOpen = false;
    waveFrames.revoke();
    waveDots.clear();
    waveBody.Children().Clear();
    waveRoot.Visibility(Visibility::Collapsed);
    if (waveReturnCard >= 0 && waveReturnCard == selectedCard)
        detailRoot.Visibility(Visibility::Visible);
    else
    {
        rosterPanel.Visibility(Visibility::Visible);
        UpdateRosterGuide();
    }
    waveReturnCard = -1;
}
void AddWaveHeading(hstring const &text)
{
    auto title = MakeText(text, 15, 0xe0c688);
    title.Margin({0, 10, 0, 6});
    waveBody.Children().Append(title);
}
void AddWaveLine(hstring const &text, double size = 13, uint32_t color = 0xe2eef3)
{
    auto line = MakeText(text, size, color);
    line.TextWrapping(TextWrapping::Wrap);
    line.Margin({0, 0, 0, 4});
    waveBody.Children().Append(line);
}
void RenderWaves(double now)
{
    waveDots.clear();
    waveBody.Children().Clear();
    waveTitle.Text(L"兵线 · " + waves.GetNamedString(L"map", L""));
    waveRules.Text(L"");
    if (now < 0)
    {
        AddWaveLine(L"等待游戏数据…（超过 5 秒未更新时暂停倒计时）", 13, 0x91acbb);
        return;
    }
    RenderWaveSpawns(now);
    RenderWaveLanes(now);
    RenderDeathCosts();
}
// Upcoming waves as cards, like departures on a timetable: countdown, kind, gold and experience.
void RenderWaveSpawns(double now)
{
    std::wstring rules;
    for (auto const &note : waves.GetNamedArray(L"notes", JsonArray()))
        rules += (rules.empty() ? L"" : L" · ") + std::wstring(note.GetString());
    waveRules.Text(hstring(rules));
    Grid row;
    row.Margin({0, 0, 0, 8});
    int column = 0;
    for (auto const &value : waves.GetNamedArray(L"spawns", JsonArray()))
    {
        auto spawn = value.GetObject();
        double time = spawn.GetNamedNumber(L"time", 0);
        if (time <= now || column >= 4)
            continue;
        row.ColumnDefinitions().Append(ColumnDefinition());
        auto card = CreateSpawnCard(spawn, time - now, column == 0);
        card.Margin({0, 0, 6, 0});
        Grid::SetColumn(card, column++);
        row.Children().Append(card);
    }
    waveBody.Children().Append(row);
}
Border CreateSpawnCard(JsonObject const &spawn, double seconds, bool next)
{
    bool cannon = spawn.GetNamedNumber(L"siege", 0) > 0;
    StackPanel body;
    body.Children().Append(
        MakeText(L"第 " + to_hstring(static_cast<int>(spawn.GetNamedNumber(L"wave", 0))) + L" 波", 11, 0x91acbb));
    auto countdown = MakeText(WaveCountdown(seconds), next ? 22 : 17, next ? 0x7ff0ee : 0xe2eef3);
    countdown.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
    body.Children().Append(countdown);
    body.Children().Append(MakeText(cannon ? L"炮车波" : L"普通波", 12, cannon ? 0xffd27a : 0xa4bcc7));
    body.Children().Append(MakeText(to_hstring(static_cast<int>(spawn.GetNamedNumber(L"gold", 0))) + L" 金 · " +
                                        to_hstring(static_cast<int>(spawn.GetNamedNumber(L"xp", 0))) + L" 经验",
                                    11,
                                    0x91acbb));
    Border card;
    card.Child(body);
    card.Background(MakeBrush(next ? 0x163f4b : 0x112f3c));
    card.BorderBrush(MakeBrush(cannon ? 0x8a6d2f : 0x23505c));
    card.BorderThickness({next ? 2.0 : 1.0});
    card.Padding({10, 6, 10, 6});
    return card;
}
void RenderWaveLanes(double now)
{
    auto heading = MakeText(L"兵线 · 点击进度条选择停靠点", 15, 0xe0c688);
    heading.Margin({0, 4, 0, 6});
    ToolTipService::SetToolTip(heading,
                               box_value(L"位置与到达时间按游戏时间推算，不代表实际兵线：游戏接口不提供小兵位置。"));
    waveBody.Children().Append(heading);
    int index = 0;
    for (auto const &value : waves.GetNamedArray(L"lanes", JsonArray()))
    {
        if (index < static_cast<int>(waveStops.size()))
            RenderWaveLane(value.GetObject(), index, now);
        ++index;
    }
}
std::vector<WaveDot> waveDots;
// A tap near a structure stops at the structure itself, so a turret's timetable is one tap away.
static double SnapStop(JsonArray const &markers, double at)
{
    for (auto const &value : markers)
    {
        double marker = value.GetObject().GetNamedNumber(L"at", 0);
        if (std::abs(marker - at) <= 0.03)
            return marker;
    }
    return at;
}
static hstring StopLabel(JsonArray const &markers, double at, bool chosen)
{
    for (auto const &value : markers)
    {
        auto marker = value.GetObject();
        if (std::abs(marker.GetNamedNumber(L"at", 0) - at) <= 0.002)
            return marker.GetNamedString(L"label", L"") +
                   (marker.GetNamedBoolean(L"alive", true) ? L"" : L"（已被摧毁）");
    }
    return chosen ? L"自选位置 · 距我方水晶 " + to_hstring(static_cast<int>(at * 100 + .5)) + L"%"
                  : hstring(L"双方前塔中点（默认）");
}
void RenderWaveLane(JsonObject const &lane, int index, double now)
{
    StackPanel card;
    card.Background(MakeBrush(0x112f3c));
    card.Padding({10, 6, 10, 6});
    card.Margin({0, 0, 0, 6});
    auto progress = LaneProgress(lane);
    auto markers = progress.GetNamedArray(L"markers", JsonArray());
    LaneSpan span(progress);
    bool chosen = waveStops[index] >= 0;
    double stop = chosen ? waveStops[index] : DefaultStop(markers);
    card.Children().Append(CreateLaneHeader(lane.GetNamedString(L"name", L""), markers, index, stop, chosen));
    if (span.length > 0)
    {
        card.Children().Append(CreateLaneBar(progress, markers, index, stop, now));
        auto timetable = progress.GetNamedArray(L"waves", JsonArray());
        card.Children().Append(CreateStopRow(timetable, span, stop, now));
    }
    RenderSiege(card, lane);
    // An inference only helps when structures have fallen; "no structures lost" is left out.
    auto hint = progress.GetNamedString(L"hint", L"");
    if (!hint.empty() && std::wstring_view(hint).find(L"无法") == std::wstring_view::npos)
        card.Children().Append(MakeText(hint, 12, 0xdbc47f));
    auto structures = lane.GetNamedString(L"structures", L"");
    if (!structures.empty())
    {
        auto text = MakeText(structures, 12, 0xff8a7a);
        text.TextWrapping(TextWrapping::Wrap);
        card.Children().Append(text);
    }
    waveBody.Children().Append(card);
}
// The player's basic attacks against the next enemy turret at full health, and that turret's shots back.
static void RenderSiege(StackPanel const &card, JsonObject const &lane)
{
    auto value = lane.TryLookup(L"siege");
    if (!value || value.ValueType() != JsonValueType::Object)
        return;
    auto siege = value.GetObject();
    auto target = siege.GetNamedString(L"target", L"");
    auto count = [&siege](wchar_t const *hits, wchar_t const *seconds)
    {
        return to_hstring(static_cast<int>(siege.GetNamedNumber(hits, 0))) + L" 下约 " +
               to_hstring(static_cast<int>(siege.GetNamedNumber(seconds, 0) + .5)) + L" 秒";
    };
    std::wstring attack = L"拆" + std::wstring(target) + L"：每下 " +
                          std::wstring(to_hstring(static_cast<int>(siege.GetNamedNumber(L"hit", 0)))) +
                          (siege.GetNamedBoolean(L"magic", false) ? L" 魔法" : L" 物理") + L" · 有兵 " +
                          std::wstring(count(L"hits", L"seconds")) + L" · 无兵 " +
                          std::wstring(count(L"hitsAlone", L"secondsAlone"));
    int fortification = static_cast<int>(siege.GetNamedNumber(L"fortification", 0) * 100 + .5);
    if (fortification > 0)
        attack += L" · 含减伤 " + std::wstring(to_hstring(fortification)) + L"%";
    std::wstring shots;
    for (auto const &shot : siege.GetNamedArray(L"shots", JsonArray()))
        shots += (shots.empty() ? L"" : L" / ") + std::wstring(to_hstring(static_cast<int>(shot.GetNumber())));
    std::wstring defense = std::wstring(target) + L"打你：" + shots + L" · 按当前血量扛 " +
                           std::wstring(to_hstring(static_cast<int>(siege.GetNamedNumber(L"shotsToDown", 0)))) + L" 下";
    StackPanel lines;
    lines.Margin({0, 2, 0, 2});
    for (auto const &text : {attack, defense})
    {
        auto line = MakeText(hstring(text), 12, 0xa4bcc7);
        line.TextWrapping(TextWrapping::Wrap);
        lines.Children().Append(line);
    }
    ToolTipService::SetToolTip(
        lines,
        box_value(L"按塔满血计算：游戏接口不提供塔的当前血量。\n"
                  L"只算普攻：攻击力 + 60% 法强，伤害类型按两者大小估计；不含技能、符文和强化。\n"
                  L"无兵指附近没有我方小兵，塔受到英雄伤害 −80%。塔打你每下 +50%，最多 +150%。"));
    card.Children().Append(lines);
}
// The lane name on the left; the stop and its reset on the right, where a timetable names its station.
Grid CreateLaneHeader(hstring const &name, JsonArray const &markers, int index, double stop, bool chosen)
{
    Grid header;
    header.ColumnDefinitions().Append(ColumnDefinition());
    ColumnDefinition stopColumn;
    stopColumn.Width({1, GridUnitType::Auto});
    header.ColumnDefinitions().Append(stopColumn);
    header.Children().Append(MakeText(name, 14, 0xe9d8a6));
    StackPanel right;
    right.Orientation(Orientation::Horizontal);
    right.VerticalAlignment(VerticalAlignment::Center);
    right.Children().Append(MakeText(L"停靠：" + StopLabel(markers, stop, chosen), 12, 0xa4bcc7));
    if (chosen)
    {
        auto reset = MakeText(L"  ↺", 14, 0x7ff0ee);
        ToolTipService::SetToolTip(reset, box_value(L"恢复默认停靠点"));
        reset.Tapped(
            [this, index](auto &&, Windows::UI::Xaml::Input::TappedRoutedEventArgs const &e)
            {
                e.Handled(true);
                waveStops[index] = -1;
                RequestWaveRender();
            });
        right.Children().Append(reset);
    }
    Grid::SetColumn(right, 1);
    header.Children().Append(right);
    return header;
}
// The next wave of each side at the stop on one line: own on the left, enemy on the right, like the bar.
Grid CreateStopRow(JsonArray const &timetable, LaneSpan const &span, double stop, double now)
{
    Grid row;
    row.Margin({0, 2, 0, 2});
    row.ColumnDefinitions().Append(ColumnDefinition());
    row.ColumnDefinitions().Append(ColumnDefinition());
    ToolTipService::SetToolTip(row, box_value(L"双方下一波兵到 ◆ 停靠点的时间；点击进度条可移动停靠点。"));
    for (bool ally : {true, false})
    {
        uint32_t color = ally ? 0x45c7ce : 0xf3b37e;
        StackPanel side;
        side.Orientation(Orientation::Horizontal);
        side.HorizontalAlignment(ally ? HorizontalAlignment::Left : HorizontalAlignment::Right);
        auto name = MakeText(ally ? L"我方 →" : L"← 敌方", 13, color);
        name.VerticalAlignment(VerticalAlignment::Center);
        name.Margin({ally ? 0.0 : 6.0, 0, ally ? 6.0 : 0.0, 0});
        auto chip = CreateArrivalChip(timetable, ally ? span.AllyDistance(stop) : span.EnemyDistance(stop), now);
        if (ally)
        {
            side.Children().Append(name);
            side.Children().Append(chip);
        }
        else
        {
            side.Children().Append(chip);
            side.Children().Append(name);
        }
        Grid::SetColumn(side, ally ? 0 : 1);
        row.Children().Append(side);
    }
    return row;
}
// The first wave not yet past a distance: its countdown, "到达中" while its minions arrive, and a cannon mark.
static Border CreateArrivalChip(JsonArray const &timetable, double distance, double now)
{
    auto next = NextArrival(timetable, distance, now);
    bool cannon = next.cannon;
    hstring text = next.arrive < 0     ? hstring(L"—")
                   : next.arrive > now ? WaveCountdown(next.arrive - now)
                                       : hstring(L"到达中");
    Border chip;
    chip.Child(MakeText(cannon ? text + L" 炮车" : text, 13, cannon ? 0xffd27a : 0xe2eef3));
    chip.Background(MakeBrush(0x143542));
    chip.CornerRadius({3, 3, 3, 3});
    chip.Padding({8, 1, 8, 1});
    chip.VerticalAlignment(VerticalAlignment::Center);
    return chip;
}
// Own nexus on the left, enemy on the right; walking waves stop at the chosen stop, where they would fight.
Grid CreateLaneBar(JsonObject const &progress, JsonArray const &markers, int index, double stop, double now)
{
    Grid bar;
    bar.Margin({0, 2, 0, 2});
    // A background makes the whole bar hit-testable, not only its markers.
    bar.Background(MakeBrush(0x112f3c));
    // Row 0 holds the countdowns above the turrets; row 1 the track, structures and waves.
    RowDefinition labels, lane;
    labels.Height({16, GridUnitType::Pixel});
    lane.Height({24, GridUnitType::Pixel});
    bar.RowDefinitions().Append(labels);
    bar.RowDefinitions().Append(lane);
    auto place = [&bar](FrameworkElement const &element, int row)
    {
        Grid::SetRow(element, row);
        bar.Children().Append(element);
    };
    Border track;
    track.Height(4);
    track.Background(MakeBrush(0x1d4c57));
    track.VerticalAlignment(VerticalAlignment::Center);
    place(track, 1);
    for (auto const &value : markers)
    {
        auto marker = value.GetObject();
        bool ally = marker.GetNamedBoolean(L"ally", false), alive = marker.GetNamedBoolean(L"alive", true);
        uint32_t color = !alive ? 0x4a5a63 : ally ? 0x45c7ce : 0xf3b37e;
        auto icon = CreateStructureIcon(MarkerKind(marker), color);
        icon.VerticalAlignment(VerticalAlignment::Center);
        auto details = marker.GetNamedString(L"details", L"");
        hstring tip = marker.GetNamedString(L"label", L"") + (alive ? L"" : L"（已被摧毁）");
        if (alive && !details.empty())
            tip = tip + L"\n" + details;
        ToolTipService::SetToolTip(icon, box_value(tip));
        place(AtFraction(icon, marker.GetNamedNumber(L"at", 0)), 1);
    }
    PlaceFortifications(bar, markers, now);
    LaneSpan span(progress);
    PlaceTurretCountdowns(progress.GetNamedArray(L"waves", JsonArray()), markers, span, stop, now, place);
    for (auto const &value : progress.GetNamedArray(L"waves", JsonArray()))
    {
        auto wave = value.GetObject();
        if (now <= wave.GetNamedNumber(L"time", 0) || span.length <= 0)
            continue;
        for (bool ally : {true, false})
        {
            WavePath path(wave, span, stop, ally);
            auto text = MakeText(L"●", 20, ally ? 0x45c7ce : 0xf3b37e);
            text.VerticalAlignment(VerticalAlignment::Center);
            auto layer = AtFraction(text, path.At(now));
            place(layer, 1);
            waveDots.push_back({layer, path});
        }
    }
    auto pin = MakeText(L"◆", 16, 0xffd27a);
    pin.VerticalAlignment(VerticalAlignment::Center);
    place(AtFraction(pin, stop), 1);
    // The sender is the bar; capturing the bar itself would keep every re-rendered bar alive.
    bar.Tapped(
        [this, index, markers](IInspectable const &sender, Windows::UI::Xaml::Input::TappedRoutedEventArgs const &e)
        {
            auto tapped = sender.as<Grid>();
            double width = tapped.ActualWidth();
            if (width <= 0)
                return;
            e.Handled(true);
            waveStops[index] = SnapStop(markers, std::clamp(e.GetPosition(tapped).X / width, 0.0, 1.0));
            RequestWaveRender();
        });
    return bar;
}
// Above each standing turret: when the side's own wave next passes it on the way out, the current wave until it
// has passed, then the next one; the countdown reaches zero as the dot on the bar passes the turret. Waves stop
// at the chosen stop to fight, so turrets beyond it get no countdown. The nexus is the spawn and gets none; an
// inhibitor gets one unless a turret of its side stands too close for two labels (Summoner's Rift side lanes).
template <typename Place>
static void PlaceTurretCountdowns(JsonArray const &timetable,
                                  JsonArray const &markers,
                                  LaneSpan const &span,
                                  double stop,
                                  double now,
                                  Place const &place)
{
    for (auto const &value : markers)
    {
        auto marker = value.GetObject();
        auto label = marker.GetNamedString(L"label", L"");
        auto kind = MarkerKind(marker);
        bool ally = marker.GetNamedBoolean(L"ally", false);
        double at = marker.GetNamedNumber(L"at", 0);
        if (!marker.GetNamedBoolean(L"alive", true) || kind == L"base" ||
            (kind == L"inhibitor" && HasCloseTurret(markers, ally, at)))
            continue;
        if (ally ? at > stop : at < stop)
            continue;
        double distance = ally ? span.AllyDistance(at) : span.EnemyDistance(at);
        if (distance < 0)
            continue;
        auto next = NextArrival(timetable, distance, now);
        if (next.arrive < 0)
            continue;
        hstring text = next.arrive > now ? WaveCountdown(next.arrive - now) : hstring(L"过");
        if (next.cannon)
            text = L"炮" + text;
        auto countdown = MakeText(text, 11, ally ? 0x45c7ce : 0xf3b37e);
        ToolTipService::SetToolTip(countdown,
                                   box_value((ally ? L"我方兵线经过" : L"敌方兵线经过") + label + L"的倒计时"));
        place(AtFraction(countdown, at), 0);
    }
}
// Below a turret whose fortification is running: the reduction and its countdown, in a row added only then.
static void PlaceFortifications(Grid const &bar, JsonArray const &markers, double now)
{
    bool added = false;
    for (auto const &value : markers)
    {
        auto marker = value.GetObject();
        auto until = marker.TryLookup(L"fortifiedUntil");
        if (!until || until.ValueType() != JsonValueType::Number || until.GetNumber() <= now)
            continue;
        if (!added)
        {
            RowDefinition row;
            row.Height({16, GridUnitType::Pixel});
            bar.RowDefinitions().Append(row);
            added = true;
        }
        int percent = static_cast<int>(marker.GetNamedNumber(L"fortification", 0) * 100 + .5);
        bool ally = marker.GetNamedBoolean(L"ally", false);
        hstring reduction = L"−" + to_hstring(percent) + L"%";
        auto text = MakeText(reduction + L" " + WaveCountdown(until.GetNumber() - now), 11, ally ? 0x45c7ce : 0xf3b37e);
        hstring tip = marker.GetNamedString(L"label", L"") + L"刚失去一塔，受到伤害 " + reduction + L"，到点后结束";
        ToolTipService::SetToolTip(text, box_value(tip));
        auto layer = AtFraction(text, marker.GetNamedNumber(L"at", 0));
        Grid::SetRow(layer, 2);
        bar.Children().Append(layer);
    }
}
// Death timers from level and game time: the cost of dying now, own team on the left.
void RenderDeathCosts()
{
    auto deaths = waves.GetNamedArray(L"deaths", JsonArray());
    if (deaths.Size() == 0)
        return;
    double factor = waves.GetNamedNumber(L"deathFactor", 0);
    AddWaveHeading(factor > 0 ? L"现在阵亡需等待 · 时间加成 +" + to_hstring(static_cast<int>(factor + .5)) + L"%"
                              : hstring(L"现在阵亡需等待"));
    RenderDeathAdvice();
    // Players of one level wait the same time, so each level is one row with its heroes on both sides.
    std::vector<int> levels;
    for (auto const &value : deaths)
    {
        int level = static_cast<int>(value.GetObject().GetNamedNumber(L"level", 0));
        if (std::find(levels.begin(), levels.end(), level) == levels.end())
            levels.push_back(level);
    }
    std::sort(levels.begin(), levels.end(), std::greater<>());
    Grid table;
    table.Background(MakeBrush(0x112f3c));
    table.Padding({10, 4, 10, 6});
    table.Margin({0, 0, 0, 6});
    for (int column = 0; column < 4; ++column)
    {
        ColumnDefinition definition;
        definition.Width({1, column < 2 ? GridUnitType::Auto : GridUnitType::Star});
        table.ColumnDefinitions().Append(definition);
    }
    auto cell = [&table](FrameworkElement const &element, int row, int column)
    {
        Grid::SetRow(element, row);
        Grid::SetColumn(element, column);
        table.Children().Append(element);
    };
    table.RowDefinitions().Append(RowDefinition());
    int column = 0;
    for (auto label : {L"等级", L"等待", L"我方", L"敌方"})
    {
        auto header = MakeText(label, 12, column == 2 ? 0x45c7ce : column == 3 ? 0xf3b37e : 0x91acbb);
        header.Margin({0, 0, 14, 2});
        cell(header, 0, column++);
    }
    int row = 1;
    for (int level : levels)
    {
        table.RowDefinitions().Append(RowDefinition());
        double seconds = 0;
        std::array<StackPanel, 2> sides;
        for (auto &side : sides)
        {
            side.Orientation(Orientation::Horizontal);
            side.Margin({0, 2, 8, 2});
        }
        for (auto const &value : deaths)
        {
            auto death = value.GetObject();
            if (static_cast<int>(death.GetNamedNumber(L"level", 0)) != level)
                continue;
            seconds = death.GetNamedNumber(L"seconds", 0);
            sides[death.GetNamedBoolean(L"ally", false) ? 0 : 1].Children().Append(CreateDeathAvatar(death));
        }
        auto levelText = MakeText(to_hstring(level) + L" 级", 13, 0xa4bcc7);
        levelText.VerticalAlignment(VerticalAlignment::Center);
        levelText.Margin({0, 0, 14, 0});
        cell(levelText, row, 0);
        auto wait = MakeText(to_hstring(static_cast<int>(seconds + .5)) + L" 秒", 15, 0xe2eef3);
        wait.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        wait.VerticalAlignment(VerticalAlignment::Center);
        wait.Margin({0, 0, 14, 0});
        cell(wait, row, 1);
        cell(sides[0], row, 2);
        cell(sides[1], row, 3);
        ++row;
    }
    waveBody.Children().Append(table);
}
// The main program's verdict on dying now to shop, its deciding reasons beside it and every factor in the tooltip.
void RenderDeathAdvice()
{
    auto value = waves.TryLookup(L"deathAdvice");
    if (!value || value.ValueType() != JsonValueType::Object)
        return;
    auto advice = value.GetObject();
    auto verdict = advice.GetNamedString(L"verdict", L"avoid");
    bool good = verdict == L"good", okay = verdict == L"okay";
    uint32_t color = good ? 0x7fd88f : okay ? 0xffd27a : 0xff8a7a;
    Border chip;
    chip.Child(MakeText(good ? L"适合送死" : okay ? L"可以送死" : L"不建议送死", 13, 0x071e29));
    chip.Background(MakeBrush(color));
    chip.CornerRadius({3, 3, 3, 3});
    chip.Padding({8, 1, 8, 1});
    chip.Margin({0, 0, 8, 0});
    chip.VerticalAlignment(VerticalAlignment::Top);
    std::wstring reasons;
    for (auto const &reason : advice.GetNamedArray(L"reasons", JsonArray()))
        reasons += (reasons.empty() ? L"" : L" · ") + std::wstring(reason.GetString());
    auto text = MakeText(hstring(reasons), 12, color);
    text.TextWrapping(TextWrapping::Wrap);
    text.VerticalAlignment(VerticalAlignment::Center);
    std::wstring details;
    for (auto const &detail : advice.GetNamedArray(L"details", JsonArray()))
        details += std::wstring(detail.GetString()) + L"\n";
    details += L"按复活时间、兵线理论到塔时间、双方阵亡和阵容推算，仅供参考。";
    Grid row;
    row.Margin({0, 0, 0, 6});
    ColumnDefinition chipColumn;
    chipColumn.Width({1, GridUnitType::Auto});
    row.ColumnDefinitions().Append(chipColumn);
    row.ColumnDefinitions().Append(ColumnDefinition());
    row.Children().Append(chip);
    Grid::SetColumn(text, 1);
    row.Children().Append(text);
    ToolTipService::SetToolTip(row, box_value(hstring(details)));
    waveBody.Children().Append(row);
}
// The hero icon the roster already loaded; the champion's first character until it arrives.
Border CreateDeathAvatar(JsonObject const &death)
{
    auto champion = death.GetNamedString(L"champion", L"");
    bool self = death.GetNamedBoolean(L"self", false);
    Border frame;
    frame.Width(30);
    frame.Height(30);
    frame.Margin({0, 0, 4, 0});
    frame.CornerRadius({4, 4, 4, 4});
    frame.Background(MakeBrush(0x1d4c57));
    frame.BorderBrush(MakeBrush(self ? 0x7ff0ee : 0x23505c));
    frame.BorderThickness({self ? 2.0 : 1.0});
    if (auto source = RosterIcon(death.GetNamedString(L"player", L""), champion))
    {
        Image image;
        image.Source(source);
        image.Stretch(Stretch::UniformToFill);
        frame.Child(image);
    }
    else
    {
        hstring letter = champion.empty() ? hstring(L"?") : hstring(std::wstring_view(champion).substr(0, 1));
        auto initial = MakeText(letter, 13, 0xe2eef3);
        initial.HorizontalAlignment(HorizontalAlignment::Center);
        initial.VerticalAlignment(VerticalAlignment::Center);
        frame.Child(initial);
    }
    ToolTipService::SetToolTip(frame,
                               box_value(champion + L" · " +
                                         to_hstring(static_cast<int>(death.GetNamedNumber(L"level", 0))) + L" 级" +
                                         (self ? L" · 自己" : L"")));
    return frame;
}
// By roster key; a main program older than the widget sends no key, so the champion name is the fallback.
ImageSource RosterIcon(hstring const &player, hstring const &champion) const
{
    for (auto const &card : cards)
    {
        if (player.empty() ? !champion.empty() && card.champion.Text() == champion : card.key == player)
            return card.icon.Source();
    }
    return nullptr;
}
// Super minion and death messages need content; the main program checks again when building the lines.
bool WaveChatAvailable(hstring const &target) const
{
    if (target == L"spawn")
        return true;
    if (target == L"deaths")
        return waves.GetNamedArray(L"deaths", JsonArray()).Size() > 0;
    if (target != L"super")
        return false;
    for (auto const &lane : waves.GetNamedArray(L"lanes", JsonArray()))
        if (!lane.GetObject().GetNamedString(L"structures", L"").empty())
            return true;
    return false;
}
