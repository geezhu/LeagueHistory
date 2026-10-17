// The "League 兵线" widget: a small bar meant to stay pinned over the game. It reads the same snapshot as the roster
// widget but ignores its show/hide commands, so hiding the roster never hides the wave bar.
#pragma once
#include "WaveShapes.h"
#include "MiniVoiceSender.h"

struct WaveMiniView : std::enable_shared_from_this<WaveMiniView>
{
    static constexpr double DefaultOpacity = .85, MinOpacity = .2;
    static constexpr double HeaderHeight = 48, LaneHeight = 28, VoiceHeight = 36, DefaultWidth = 320;
    static constexpr wchar_t const *CollapsedSetting = L"waveMiniCollapsed";
    static constexpr uint32_t Background = 0x071e29;

    Grid root;
    TextBlock status = MakeText(L"等待 League 主程序连接…", 13, 0x91acbb);
    StackPanel nextPanel;
    TextBlock nextCountdown = MakeText(L"", 20, 0x7ff0ee);
    TextBlock nextKind = MakeText(L"", 13, 0xa4bcc7);
    TextBlock nextNumber = MakeText(L"", 11, 0x91acbb);
    TextBlock upcoming = MakeText(L"", 12, 0xffd27a);
    TextBlock death = MakeText(L"", 12, 0xa4bcc7);
    Button toggle;
    StackPanel lanes;
    std::shared_ptr<MiniVoiceSender> voice = std::make_shared<MiniVoiceSender>();
    XboxGameBarWidget widget{nullptr};
    DispatcherTimer timer;
    CompositionTarget::Rendering_revoker frames;
    std::vector<WaveDot> dots;
    JsonObject waves;
    hstring readError;
    double opacity = DefaultOpacity;
    int64_t lastGood = 0, renderedSecond = -1;
    // Wall clock when the own champion respawns; zero while alive.
    int64_t respawnUntil = 0;
    int renderedLanes = -1;
    hstring session;
    bool busy = false, stopped = false, collapsed = false, enabled = false, active = false;
    // The main program accepts confirmed voice text; the voice row shows only then and during a game.
    bool supportsVoice = false, voiceShown = false;
    // Preview captures read a fixture with a frozen clock; nothing is sent to the game.
    bool preview = false, capturePending = false;

    WaveMiniView()
    {
        auto stored = ApplicationData::Current().LocalSettings().Values().TryLookup(CollapsedSetting);
        collapsed = stored && unbox_value_or<bool>(stored, false);
        InitializeRoot();
    }
    void InitializeRoot()
    {
        root.RequestedTheme(ElementTheme::Dark);
        root.Padding({10, 6, 8, 6});
        RowDefinition header, body, voiceRow;
        header.Height({1, GridUnitType::Auto});
        voiceRow.Height({1, GridUnitType::Auto});
        root.RowDefinitions().Append(header);
        root.RowDefinitions().Append(body);
        root.RowDefinitions().Append(voiceRow);
        root.Children().Append(CreateHeader());
        lanes.Margin({0, 4, 0, 0});
        Grid::SetRow(lanes, 1);
        root.Children().Append(lanes);
        voice->row.Visibility(Visibility::Collapsed);
        Grid::SetRow(voice->row, 2);
        root.Children().Append(voice->row);
        ApplyBackground();
    }
    // Next wave on the left, the next cannon or super minion lanes beside it, the own death time and the collapse
    // toggle on the right. Only the upcoming text shrinks when the widget is narrow.
    Grid CreateHeader()
    {
        Grid header;
        header.MinHeight(HeaderHeight - 12);
        ColumnDefinition nextColumn, upcomingColumn, deathColumn, toggleColumn;
        nextColumn.Width({1, GridUnitType::Auto});
        deathColumn.Width({1, GridUnitType::Auto});
        toggleColumn.Width({1, GridUnitType::Auto});
        for (auto const &column : {nextColumn, upcomingColumn, deathColumn, toggleColumn})
            header.ColumnDefinitions().Append(column);
        status.VerticalAlignment(VerticalAlignment::Center);
        status.TextWrapping(TextWrapping::Wrap);
        status.MaxLines(2);
        Grid::SetColumnSpan(status, 3);
        header.Children().Append(status);
        nextPanel.Orientation(Orientation::Horizontal);
        nextPanel.VerticalAlignment(VerticalAlignment::Center);
        nextCountdown.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        for (auto const &text : {nextCountdown, nextKind, nextNumber})
        {
            text.VerticalAlignment(VerticalAlignment::Center);
            text.Margin({0, 0, 8, 0});
            nextPanel.Children().Append(text);
        }
        nextPanel.Visibility(Visibility::Collapsed);
        header.Children().Append(nextPanel);
        upcoming.VerticalAlignment(VerticalAlignment::Center);
        upcoming.Margin({0, 0, 6, 0});
        Grid::SetColumn(upcoming, 1);
        header.Children().Append(upcoming);
        death.VerticalAlignment(VerticalAlignment::Center);
        death.Margin({0, 0, 6, 0});
        Grid::SetColumn(death, 2);
        header.Children().Append(death);
        toggle.Background(MakeBrush(0x143542));
        toggle.BorderThickness({0});
        toggle.Padding({8, 2, 8, 2});
        toggle.VerticalAlignment(VerticalAlignment::Center);
        toggle.Click([this](auto &&, auto &&) { SetCollapsed(!collapsed); });
        Grid::SetColumn(toggle, 3);
        header.Children().Append(toggle);
        RefreshToggle();
        return header;
    }
    void Start(XboxGameBarWidget const &value)
    {
        widget = value;
        if (widget)
        {
            widget.MinWindowSize({220, static_cast<float>(HeaderHeight)});
            widget.MaxWindowSize({640, 300});
            voice->widget = widget;
            widget.GameBarDisplayModeChanged(
                [weak = weak_from_this()](auto &&, auto &&)
                {
                    if (auto self = weak.lock())
                        self->root.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal,
                                                         [weak]
                                                         {
                                                             if (auto view = weak.lock(); view && !view->stopped)
                                                                 view->voice->DisplayModeChanged();
                                                         });
                });
            widget.RequestedOpacityChanged(
                [weak = weak_from_this()](auto &&, auto &&)
                {
                    if (auto self = weak.lock())
                        self->root.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal,
                                                         [weak]
                                                         {
                                                             if (auto view = weak.lock(); view && !view->stopped)
                                                                 view->ApplyBackground();
                                                         });
                });
        }
        timer.Interval(std::chrono::milliseconds(500));
        timer.Tick(
            [weak = weak_from_this()](auto &&, auto &&)
            {
                if (auto self = weak.lock())
                {
                    self->Read();
                    self->Update(false);
                    self->voice->Tick();
                }
            });
        timer.Start();
        Read();
        Trace(L"wave-mini-started collapsed=" + to_hstring(collapsed));
    }
    void ConfigurePreview(hstring const &argument)
    {
        preview = true;
        capturePending = true;
        collapsed = std::wstring_view(argument).ends_with(L":collapsed");
        root.Width(DefaultWidth);
        root.HorizontalAlignment(HorizontalAlignment::Left);
        root.VerticalAlignment(VerticalAlignment::Top);
        RefreshToggle();
        // A sample result shows the voice row's layout; no request, audio or chat is produced.
        voice->Preview(L"先别开团，等队友到齐后一起推");
    }
    void Stop()
    {
        voice->Stop();
        stopped = true;
        timer.Stop();
        frames.revoke();
        dots.clear();
        widget = nullptr;
    }
    // Only the background fades; text and icons stay opaque so they remain readable over the game. Game Bar's own
    // widget transparency multiplies the main program's setting.
    void ApplyBackground()
    {
        double requested = widget ? widget.RequestedOpacity() : 1.0;
        auto alpha = static_cast<uint8_t>(std::clamp(opacity * requested, 0.0, 1.0) * 255 + .5);
        root.Background(
            SolidColorBrush(Color{alpha, uint8_t(Background >> 16), uint8_t(Background >> 8), uint8_t(Background)}));
    }
    void RefreshToggle()
    {
        toggle.Content(MakeText(collapsed ? L"▾" : L"▴", 13));
        ToolTipService::SetToolTip(toggle, box_value(collapsed ? L"展开三路兵线" : L"收起为一行"));
        lanes.Visibility(collapsed ? Visibility::Collapsed : Visibility::Visible);
    }
    void SetCollapsed(bool value)
    {
        collapsed = value;
        ApplicationData::Current().LocalSettings().Values().Insert(CollapsedSetting, box_value(collapsed));
        RefreshToggle();
        Trace(collapsed ? L"wave-mini-collapsed" : L"wave-mini-expanded");
        ClearLanes();
        Update(true);
        FitWindow();
    }
    // Collapsed keeps one line; expanded grows by one row per lane, and the voice row adds one more while shown. The
    // width stays where the player left it.
    fire_and_forget FitWindow()
    {
        if (stopped || !widget)
            co_return;
        auto lifetime = shared_from_this();
        int laneCount =
            collapsed ? 0 : (std::max)(1, static_cast<int>(waves.GetNamedArray(L"lanes", JsonArray()).Size()));
        double width = root.ActualWidth() > 0 ? root.ActualWidth() : DefaultWidth;
        double height = HeaderHeight + laneCount * LaneHeight + (laneCount ? 8 : 0) + (voiceShown ? VoiceHeight : 0);
        Size size{static_cast<float>(width), static_cast<float>(height)};
        try
        {
            if (!co_await widget.TryResizeWindowAsync(size))
                Trace(L"wave-mini-resize-refused");
        }
        catch (hresult_error const &e)
        {
            // Game Bar may refuse while it is closing the widget; the player can still resize by hand.
            Trace(L"wave-mini-resize-error " + e.message());
        }
    }
    fire_and_forget Read()
    {
        if (busy || stopped)
            co_return;
        auto lifetime = shared_from_this();
        busy = true;
        try
        {
            auto data = co_await ReadLocalSnapshotAsync(preview ? L"detail-preview-snapshot.json" : L"snapshot.json");
            if (!stopped)
            {
                Apply(data);
                if (!readError.empty())
                    Trace(L"wave-mini-read-recovered");
                readError = L"";
            }
        }
        catch (hresult_error const &exception)
        {
            hstring error = to_hstring(uint32_t(exception.code())) + L" " + exception.message();
            // Logged once per distinct error; a missing snapshot before the main program starts is expected.
            if (error != readError)
                Trace(L"wave-mini-read-error " + error);
            readError = error;
        }
        busy = false;
        if (!stopped)
        {
            Update(false);
            CaptureWhenReady();
        }
    }
    void Apply(JsonObject const &data)
    {
        double age = preview ? 0 : Now() - data.GetNamedNumber(L"updatedAt", 0);
        if (data.GetNamedNumber(L"protocol", 0) != 1 || age < 0 || age > 15000)
            return;
        lastGood = preview ? Now() : static_cast<int64_t>(data.GetNamedNumber(L"updatedAt", 0));
        enabled = data.GetNamedBoolean(L"enabled", false);
        active = data.GetNamedBoolean(L"active", false);
        session = data.GetNamedString(L"session", L"");
        supportsVoice = data.GetNamedNumber(L"chatProtocol", 0) == 1 && data.GetNamedBoolean(L"chatVoice", false) &&
                        data.GetNamedNumber(L"voiceProtocol", 0) == 2;
        auto settings = data.TryLookup(L"waveWidget");
        double nextOpacity = settings && settings.ValueType() == JsonValueType::Object
                                 ? settings.GetObject().GetNamedNumber(L"opacity", DefaultOpacity)
                                 : DefaultOpacity;
        nextOpacity = std::clamp(nextOpacity, MinOpacity, 1.0);
        if (nextOpacity != opacity)
        {
            opacity = nextOpacity;
            ApplyBackground();
        }
        auto value = data.TryLookup(L"waves");
        auto next = value && value.ValueType() == JsonValueType::Object ? value.GetObject() : JsonObject();
        if (next.Stringify() != waves.Stringify())
        {
            waves = next;
            renderedSecond = -1;
        }
        ApplyRespawn(data.GetNamedArray(L"players", JsonArray()));
    }
    // The respawn countdown is relative to the snapshot time, like the roster widget's cards.
    void ApplyRespawn(JsonArray const &players)
    {
        respawnUntil = 0;
        for (auto const &value : players)
        {
            if (value.ValueType() != JsonValueType::Object)
                continue;
            auto player = value.GetObject();
            double respawn = player.GetNamedNumber(L"respawn", 0);
            if (player.GetNamedBoolean(L"self", false) && respawn > 0)
                respawnUntil = lastGood + static_cast<int64_t>(respawn * 1000);
        }
    }
    // Dead: the live respawn countdown. Alive: how long dying now would cost, from level and game time, with the
    // main program's die-to-shop advice in the tooltip. Empty for modes without a death timer table.
    void RenderDeath()
    {
        int64_t left = respawnUntil - Now();
        if (left > 0)
        {
            death.Text(L"复活 " + to_hstring((left + 999) / 1000));
            death.Foreground(MakeBrush(0xff8a7a));
            ToolTipService::SetToolTip(death, box_value(L"你的英雄复活倒计时"));
            return;
        }
        for (auto const &value : waves.GetNamedArray(L"deaths", JsonArray()))
        {
            auto cost = value.GetObject();
            if (!cost.GetNamedBoolean(L"self", false))
                continue;
            death.Text(L"阵亡 " + to_hstring(static_cast<int>(cost.GetNamedNumber(L"seconds", 0) + .5)) + L" 秒");
            death.Foreground(MakeBrush(0xa4bcc7));
            ToolTipService::SetToolTip(death, box_value(DeathTip(cost)));
            return;
        }
        death.Text(L"");
    }
    hstring DeathTip(JsonObject const &cost) const
    {
        std::wstring tip = L"现在阵亡需等待的复活时间（" +
                           std::wstring(to_hstring(static_cast<int>(cost.GetNamedNumber(L"level", 0)))) +
                           L" 级，按等级与游戏时间推算）";
        auto advice = waves.TryLookup(L"deathAdvice");
        if (!advice || advice.ValueType() != JsonValueType::Object)
            return hstring(tip);
        auto verdict = advice.GetObject().GetNamedString(L"verdict", L"avoid");
        tip += verdict == L"good" ? L"\n适合送死" : verdict == L"okay" ? L"\n可以送死" : L"\n不建议送死";
        for (auto const &reason : advice.GetObject().GetNamedArray(L"reasons", JsonArray()))
            tip += L" · " + std::wstring(reason.GetString());
        return hstring(tip);
    }
    // Why there is no countdown, in the order the player can fix it; empty when waves are counting down.
    hstring StatusText(double now) const
    {
        if (lastGood == 0 || Now() - lastGood > 15000)
            return L"未连接 League 主程序；请保持 League 运行。";
        if (!enabled)
            return L"请在 League「设置 → 对局速览」选择 Game Bar。";
        if (!active)
            return L"等待进入对局…";
        if (!WaveDataReady(waves))
            return L"当前模式没有兵线。";
        if (now < 0)
            return L"等待游戏数据…";
        return L"";
    }
    void Update(bool force)
    {
        if (stopped)
            return;
        bool connected = lastGood > 0 && Now() - lastGood <= 15000 && enabled && active;
        double now = connected && WaveDataReady(waves) ? WaveGameNow(waves, preview) : -1;
        auto text = StatusText(now);
        status.Text(text);
        status.Visibility(text.empty() ? Visibility::Collapsed : Visibility::Visible);
        nextPanel.Visibility(text.empty() ? Visibility::Visible : Visibility::Collapsed);
        upcoming.Visibility(text.empty() ? Visibility::Visible : Visibility::Collapsed);
        death.Visibility(text.empty() ? Visibility::Visible : Visibility::Collapsed);
        UpdateVoice(connected);
        if (!text.empty())
        {
            ClearLanes();
            return;
        }
        RenderNext(now);
        RenderDeath();
        int64_t second = static_cast<int64_t>(now);
        if (collapsed || (!force && second == renderedSecond))
            return;
        renderedSecond = second;
        RenderLanes(now);
    }
    // The voice row appears in a game when the main program supports confirmed voice text, independent of the wave
    // countdown, so a stale wave poll never cancels a recording. Showing or hiding it refits the window height.
    void UpdateVoice(bool inGame)
    {
        bool available = !preview && widget && inGame && supportsVoice;
        if (!preview)
            voice->Update(available, session);
        bool shown = available || (preview && inGame);
        voice->row.Visibility(shown ? Visibility::Visible : Visibility::Collapsed);
        if (shown != voiceShown)
        {
            voiceShown = shown;
            FitWindow();
        }
    }
    void RenderNext(double now)
    {
        auto next = NextSpawn(waves, now);
        if (!next)
        {
            nextCountdown.Text(L"—");
            nextKind.Text(L"");
            nextNumber.Text(L"");
            upcoming.Text(L"");
            return;
        }
        bool cannon = next.GetNamedNumber(L"siege", 0) > 0;
        nextCountdown.Text(WaveCountdown(next.GetNamedNumber(L"time", 0) - now));
        nextCountdown.Foreground(MakeBrush(cannon ? 0xffd27a : 0x7ff0ee));
        nextKind.Text(cannon ? L"炮车波" : L"普通波");
        nextKind.Foreground(MakeBrush(cannon ? 0xffd27a : 0xa4bcc7));
        nextNumber.Text(L"第 " + to_hstring(static_cast<int>(next.GetNamedNumber(L"wave", 0))) + L" 波");
        upcoming.Text(UpcomingText(now, cannon));
    }
    // What the player plans around after the next wave: lanes spawning super minions, else the next cannon wave.
    hstring UpcomingText(double now, bool nextIsCannon) const
    {
        std::wstring super;
        for (auto const &value : waves.GetNamedArray(L"lanes", JsonArray()))
        {
            auto lane = value.GetObject();
            if (!lane.GetNamedString(L"structures", L"").empty())
                super += (super.empty() ? L"" : L"/") + std::wstring(lane.GetNamedString(L"name", L""));
        }
        if (!super.empty())
            return hstring(L"超级兵 " + super);
        if (nextIsCannon)
            return L"";
        bool first = true;
        for (auto const &value : waves.GetNamedArray(L"spawns", JsonArray()))
        {
            auto spawn = value.GetObject();
            double time = spawn.GetNamedNumber(L"time", 0);
            if (time <= now)
                continue;
            if (!first && spawn.GetNamedNumber(L"siege", 0) > 0)
                return L"炮车 " + WaveCountdown(time - now);
            first = false;
        }
        return L"";
    }
    void ClearLanes()
    {
        frames.revoke();
        dots.clear();
        lanes.Children().Clear();
        renderedSecond = -1;
    }
    void RenderLanes(double now)
    {
        dots.clear();
        lanes.Children().Clear();
        auto all = waves.GetNamedArray(L"lanes", JsonArray());
        for (auto const &value : all)
            lanes.Children().Append(CreateLane(value.GetObject(), now));
        if (!frames)
        {
            frames =
                CompositionTarget::Rendering(auto_revoke,
                                             [weak = weak_from_this()](auto &&, auto &&)
                                             {
                                                 if (auto self = weak.lock(); self && !self->stopped)
                                                     MoveWaveDots(self->dots, WaveGameNow(self->waves, self->preview));
                                             });
        }
        // A lane count arriving after the toggle (first snapshot, mode change) refits the expanded window once.
        if (static_cast<int>(all.Size()) != renderedLanes)
        {
            renderedLanes = static_cast<int>(all.Size());
            FitWindow();
        }
    }
    // Name, the nexus-to-nexus bar with structures and walking waves, and when the own next wave reaches the
    // default stop, halfway between both sides' outermost standing structures.
    Grid CreateLane(JsonObject const &lane, double now)
    {
        Grid row;
        row.Height(LaneHeight);
        ColumnDefinition nameColumn, barColumn, chipColumn;
        nameColumn.Width({34, GridUnitType::Pixel});
        chipColumn.Width({48, GridUnitType::Pixel});
        row.ColumnDefinitions().Append(nameColumn);
        row.ColumnDefinitions().Append(barColumn);
        row.ColumnDefinitions().Append(chipColumn);
        auto structures = lane.GetNamedString(L"structures", L"");
        auto name = MakeText(lane.GetNamedString(L"name", L""), 12, structures.empty() ? 0xe9d8a6 : 0xff8a7a);
        name.VerticalAlignment(VerticalAlignment::Center);
        if (!structures.empty())
            ToolTipService::SetToolTip(name, box_value(structures));
        row.Children().Append(name);
        auto progress = LaneProgress(lane);
        LaneSpan span(progress);
        if (span.length <= 0)
            return row;
        auto markers = progress.GetNamedArray(L"markers", JsonArray());
        double stop = DefaultStop(markers);
        auto bar = CreateBar(progress, markers, span, stop, now);
        Grid::SetColumn(bar, 1);
        row.Children().Append(bar);
        auto next = NextArrival(progress.GetNamedArray(L"waves", JsonArray()), span.AllyDistance(stop), now);
        hstring text = next.arrive < 0     ? hstring(L"—")
                       : next.arrive > now ? WaveCountdown(next.arrive - now)
                                           : hstring(L"交战");
        auto chip = MakeText(next.cannon ? L"炮" + text : text, 12, next.cannon ? 0xffd27a : 0xe2eef3);
        chip.HorizontalAlignment(HorizontalAlignment::Right);
        chip.VerticalAlignment(VerticalAlignment::Center);
        ToolTipService::SetToolTip(chip, box_value(L"我方下一波到达双方前塔中点的时间（无人干扰时的估计）"));
        Grid::SetColumn(chip, 2);
        row.Children().Append(chip);
        return row;
    }
    Grid CreateBar(JsonObject const &progress, JsonArray const &markers, LaneSpan const &span, double stop, double now)
    {
        Grid bar;
        bar.Margin({0, 0, 6, 0});
        Border track;
        track.Height(3);
        track.Background(MakeBrush(0x1d4c57));
        track.VerticalAlignment(VerticalAlignment::Center);
        bar.Children().Append(track);
        for (auto const &value : markers)
        {
            auto marker = value.GetObject();
            bool ally = marker.GetNamedBoolean(L"ally", false), alive = marker.GetNamedBoolean(L"alive", true);
            auto icon = CreateStructureIcon(MarkerKind(marker), !alive ? 0x4a5a63 : ally ? 0x45c7ce : 0xf3b37e);
            icon.VerticalAlignment(VerticalAlignment::Center);
            icon.Opacity(alive ? 1 : .6);
            bar.Children().Append(AtFraction(icon, marker.GetNamedNumber(L"at", 0)));
        }
        auto pin = MakeText(L"◆", 10, 0xffd27a);
        pin.VerticalAlignment(VerticalAlignment::Center);
        bar.Children().Append(AtFraction(pin, stop));
        for (auto const &value : progress.GetNamedArray(L"waves", JsonArray()))
        {
            auto wave = value.GetObject();
            if (now <= wave.GetNamedNumber(L"time", 0))
                continue;
            for (bool ally : {true, false})
            {
                WavePath path(wave, span, stop, ally);
                auto dot = MakeText(L"●", 14, ally ? 0x45c7ce : 0xf3b37e);
                dot.VerticalAlignment(VerticalAlignment::Center);
                auto layer = AtFraction(dot, path.At(now));
                bar.Children().Append(layer);
                dots.push_back({layer, path});
            }
        }
        return bar;
    }
    void CaptureWhenReady()
    {
        if (!capturePending || root.ActualWidth() <= 0 || StatusText(WaveGameNow(waves, preview)) != L"")
            return;
        capturePending = false;
        CapturePreview();
    }
    fire_and_forget CapturePreview()
    {
        auto lifetime = shared_from_this();
        try
        {
            using namespace Windows::Graphics::Imaging;
            RenderTargetBitmap rendered;
            co_await rendered.RenderAsync(root);
            auto buffer = co_await rendered.GetPixelsAsync();
            std::vector<uint8_t> pixels(buffer.Length());
            DataReader::FromBuffer(buffer).ReadBytes(pixels);
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                collapsed ? L"wave-mini-preview-collapsed.png" : L"wave-mini-preview-expanded.png",
                CreationCollisionOption::ReplaceExisting);
            auto stream = co_await file.OpenAsync(FileAccessMode::ReadWrite);
            auto encoder = co_await BitmapEncoder::CreateAsync(BitmapEncoder::PngEncoderId(), stream);
            encoder.SetPixelData(BitmapPixelFormat::Bgra8,
                                 BitmapAlphaMode::Premultiplied,
                                 rendered.PixelWidth(),
                                 rendered.PixelHeight(),
                                 96,
                                 96,
                                 pixels);
            co_await encoder.FlushAsync();
            Trace(L"wave-mini-preview-captured " + to_hstring(rendered.PixelWidth()) + L"x" +
                  to_hstring(rendered.PixelHeight()));
        }
        catch (hresult_error const &e)
        {
            Trace(L"wave-mini-preview-error " + e.message());
        }
    }
};
