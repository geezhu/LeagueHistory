// Included inside WidgetView. The main program computes wave times from the rules; this page only counts down.
Grid waveRoot;
StackPanel waveBody;
ScrollViewer waveScroll;
Button waveButton;
TextBlock waveTitle = MakeText(L"兵线", 20, 0xe9d8a6);
JsonObject waves;
bool waveOpen = false;
int waveReturnCard = -1;
int64_t waveRenderedSecond = -1;
static constexpr int64_t WaveStaleMilliseconds = 5000;

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
bool HasWaves() const
{
    return waves.GetNamedNumber(L"protocol", 0) == 1 && waves.GetNamedArray(L"spawns", JsonArray()).Size() > 0;
}
// Game seconds now, extrapolated from the snapshot; negative when the data is too old to count down.
double WaveGameTime()
{
    int64_t age = fixturePreview ? 0 : Now() - static_cast<int64_t>(waves.GetNamedNumber(L"clock", 0));
    if (age < 0 || age > WaveStaleMilliseconds)
        return -1;
    return waves.GetNamedNumber(L"gameTime", 0) + age / 1000.0;
}
static hstring WaveClock(double seconds)
{
    int total = static_cast<int>(seconds + .5);
    wchar_t text[16];
    swprintf_s(text, L"%d:%02d", total / 60, total % 60);
    return text;
}
static hstring WaveCountdown(double seconds)
{
    return seconds > 0 ? WaveClock(seconds) : hstring(L"0:00");
}
// The first spawn still ahead; the snapshot lists a few so the countdown survives a missed poll.
JsonObject NextWave(double now)
{
    for (auto const &value : waves.GetNamedArray(L"spawns", JsonArray()))
    {
        auto spawn = value.GetObject();
        if (spawn.GetNamedNumber(L"time", 0) > now)
            return spawn;
    }
    return nullptr;
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
        next = NextWave(now);
    waveButton.IsEnabled(HasWaves());
    hstring label = L"兵线";
    if (next)
        label = label + L" " + WaveCountdown(next.GetNamedNumber(L"time", 0) - now);
    waveButton.Content(MakeText(label, 13));
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
void OpenWaves()
{
    if (!HasWaves())
        return;
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
}
// Back returns to the roster or the hero that was open, without re-requesting its details.
void CloseWaves()
{
    if (!waveOpen)
        return;
    waveOpen = false;
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
    waveBody.Children().Clear();
    waveTitle.Text(L"兵线 · " + waves.GetNamedString(L"map", L""));
    if (now < 0)
    {
        AddWaveLine(L"等待游戏数据…（超过 5 秒未更新时暂停倒计时）", 13, 0x91acbb);
        return;
    }
    AddWaveLine(L"按游戏时间推算；到达时间为无人干扰时的估计，不代表兵线实际位置。", 12, 0x91acbb);
    RenderWaveSpawns(now);
    RenderWaveLanes(now);
    RenderDeathCosts();
}
void RenderWaveSpawns(double now)
{
    AddWaveHeading(L"出波");
    auto next = NextWave(now);
    if (next)
    {
        AddWaveLine(L"下一波 " + WaveCountdown(next.GetNamedNumber(L"time", 0) - now) + L" · 第 " +
                        to_hstring(static_cast<int>(next.GetNamedNumber(L"wave", 0))) + L" 波 · " +
                        next.GetNamedString(L"label", L""),
                    18,
                    0x7ff0ee);
    }
    std::wstring later;
    for (auto const &value : waves.GetNamedArray(L"spawns", JsonArray()))
    {
        auto spawn = value.GetObject();
        double time = spawn.GetNamedNumber(L"time", 0);
        if (time <= now || (next && time == next.GetNamedNumber(L"time", 0)))
            continue;
        later += (later.empty() ? L"" : L"   ") + std::wstring(WaveClock(time)) + L" " +
                 std::wstring(spawn.GetNamedString(L"label", L""));
    }
    if (!later.empty())
        AddWaveLine(L"之后 " + hstring(later), 12, 0xa4bcc7);
    for (auto const &note : waves.GetNamedArray(L"notes", JsonArray()))
        AddWaveLine(note.GetString(), 12, 0xdbc47f);
    auto value = waves.GetNamedString(L"value", L"");
    if (!value.empty())
        AddWaveLine(L"每波价值 · " + value, 12, 0xa4bcc7);
}
hstring WaveArrivalText(hstring const &side, JsonObject const &arrival, double now)
{
    double from = arrival.GetNamedNumber(L"from", 0), to = arrival.GetNamedNumber(L"to", 0);
    hstring when = from > now ? WaveCountdown(from - now) + L" 后"
                   : to > now ? hstring(L"正在到达")
                              : hstring(L"已到达");
    return side + L" → " + arrival.GetNamedString(L"target", L"") + L"   约 " + WaveClock(from) + L"–" + WaveClock(to) +
           L"（" + when + L"）";
}
void RenderWaveLanes(double now)
{
    AddWaveHeading(L"各路兵线 · 当前进度与下一波到塔时间");
    for (auto const &value : waves.GetNamedArray(L"lanes", JsonArray()))
    {
        auto lane = value.GetObject();
        StackPanel card;
        card.Background(MakeBrush(0x112f3c));
        card.Padding({10, 6, 10, 6});
        card.Margin({0, 0, 0, 6});
        card.Children().Append(MakeText(lane.GetNamedString(L"name", L""), 14, 0xe9d8a6));
        auto progress = lane.TryLookup(L"progress");
        if (progress && progress.ValueType() == JsonValueType::Object)
            RenderLaneProgress(card, progress.GetObject(), now);
        auto ally =
            MakeText(WaveArrivalText(L"我方兵线", lane.GetNamedObject(L"ally", JsonObject()), now), 13, 0x45c7ce);
        auto enemy =
            MakeText(WaveArrivalText(L"敌方兵线", lane.GetNamedObject(L"enemy", JsonObject()), now), 13, 0xf3b37e);
        card.Children().Append(ally);
        card.Children().Append(enemy);
        auto structures = lane.GetNamedString(L"structures", L"");
        if (!structures.empty())
        {
            auto text = MakeText(structures, 12, 0xff8a7a);
            text.TextWrapping(TextWrapping::Wrap);
            card.Children().Append(text);
        }
        waveBody.Children().Append(card);
    }
}
// A child placed at a fraction of the bar with star columns, so it follows resizes without a Canvas.
static Grid AtFraction(UIElement const &child, double at)
{
    at = std::clamp(at, 0.0, 1.0);
    Grid layer;
    ColumnDefinition left, middle, right;
    left.Width({at, GridUnitType::Star});
    middle.Width({1, GridUnitType::Auto});
    right.Width({1 - at, GridUnitType::Star});
    layer.ColumnDefinitions().Append(left);
    layer.ColumnDefinitions().Append(middle);
    layer.ColumnDefinitions().Append(right);
    Grid::SetColumn(child.as<FrameworkElement>(), 1);
    layer.Children().Append(child);
    return layer;
}
// Own inhibitor on the left, enemy on the right; both latest waves walk toward the middle in game time.
void RenderLaneProgress(StackPanel const &card, JsonObject const &progress, double now)
{
    Grid bar;
    bar.Height(22);
    bar.Margin({0, 4, 0, 2});
    Border track;
    track.Height(4);
    track.Background(MakeBrush(0x1d4c57));
    track.VerticalAlignment(VerticalAlignment::Center);
    bar.Children().Append(track);
    for (auto const &value : progress.GetNamedArray(L"markers", JsonArray()))
    {
        auto marker = value.GetObject();
        bool ally = marker.GetNamedBoolean(L"ally", false), alive = marker.GetNamedBoolean(L"alive", true);
        auto text = MakeText(L"▮", 13, !alive ? 0x4a5a63 : ally ? 0x45c7ce : 0xf3b37e);
        text.VerticalAlignment(VerticalAlignment::Center);
        ToolTipService::SetToolTip(text,
                                   box_value(marker.GetNamedString(L"label", L"") + (alive ? L"" : L"（已被摧毁）")));
        bar.Children().Append(AtFraction(text, marker.GetNamedNumber(L"at", 0)));
    }
    auto current = waves.TryLookup(L"current");
    double length = progress.GetNamedNumber(L"length", 1), speed = progress.GetNamedNumber(L"speed", 1);
    double bonus = progress.GetNamedNumber(L"bonus", 0), half = length / 2;
    std::wstring line = L"首波尚未出发";
    if (current && current.ValueType() == JsonValueType::Object && length > 0 && speed > 0)
    {
        auto wave = current.GetObject();
        double start = wave.GetNamedNumber(L"time", 0), elapsed = now - start;
        double walked = elapsed > 0 ? (std::min)(half, speed * elapsed + bonus) : 0;
        double meet = start + (std::max)(0.0, half - bonus) / speed;
        int number = static_cast<int>(wave.GetNamedNumber(L"wave", 0));
        for (bool ally : {true, false})
        {
            auto dot = MakeText(L"●", 22, ally ? 0x45c7ce : 0xf3b37e);
            dot.VerticalAlignment(VerticalAlignment::Center);
            bar.Children().Append(AtFraction(dot, ally ? walked / length : 1 - walked / length));
        }
        line = walked < half
                   ? L"第 " + std::to_wstring(number) + L" 波已出发 " + std::to_wstring(static_cast<int>(elapsed)) +
                         L" 秒 · 约 " + std::wstring(WaveClock(meet)) + L" 在路中间相遇"
                   : L"第 " + std::to_wstring(number) + L" 波约 " + std::wstring(WaveClock(meet)) +
                         L" 在路中间相遇，交战中";
    }
    card.Children().Append(bar);
    card.Children().Append(MakeText(hstring(line) + L"（无人干扰时的理论位置）", 12, 0xa4bcc7));
    auto hint = MakeText(progress.GetNamedString(L"hint", L""), 12, 0xdbc47f);
    hint.TextWrapping(TextWrapping::Wrap);
    card.Children().Append(hint);
}
// Death timers from level and game time: the cost of dying now, own team on the left.
void RenderDeathCosts()
{
    auto deaths = waves.GetNamedArray(L"deaths", JsonArray());
    if (deaths.Size() == 0)
        return;
    double factor = waves.GetNamedNumber(L"deathFactor", 0);
    AddWaveHeading(L"现在阵亡需等待");
    AddWaveLine(factor > 0 ? L"按等级计算 · 时间加成 +" + to_hstring(static_cast<int>(factor + .5)) + L"%"
                           : hstring(L"按等级计算 · 尚无时间加成"),
                12,
                0x91acbb);
    Grid teamsGrid;
    teamsGrid.ColumnDefinitions().Append(ColumnDefinition());
    teamsGrid.ColumnDefinitions().Append(ColumnDefinition());
    for (int column = 0; column < 2; ++column)
    {
        bool ally = column == 0;
        StackPanel team;
        team.Background(MakeBrush(0x112f3c));
        team.Padding({10, 6, 10, 6});
        team.Margin({0, 0, ally ? 6.0 : 0.0, 6});
        team.Children().Append(MakeText(ally ? L"我方" : L"敌方", 14, ally ? 0x45c7ce : 0xf3b37e));
        for (auto const &value : deaths)
        {
            auto death = value.GetObject();
            if (death.GetNamedBoolean(L"ally", false) != ally)
                continue;
            int seconds = static_cast<int>(death.GetNamedNumber(L"seconds", 0) + .5);
            auto line = MakeText(death.GetNamedString(L"champion", L"") + L"  " +
                                     to_hstring(static_cast<int>(death.GetNamedNumber(L"level", 0))) + L" 级  " +
                                     to_hstring(seconds) + L" 秒",
                                 13,
                                 death.GetNamedBoolean(L"self", false) ? 0x7ff0ee : 0xe2eef3);
            team.Children().Append(line);
        }
        Grid::SetColumn(team, column);
        teamsGrid.Children().Append(team);
    }
    waveBody.Children().Append(teamsGrid);
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
