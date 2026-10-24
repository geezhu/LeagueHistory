// Game Bar owns this window. Never activate or reposition the desktop League window here.
#include "WidgetCommon.h"
#include "WaveModel.h"
#include "WaveShapes.h"
#include "VoiceProtocol.h"
#include "WaveMiniView.h"

struct WidgetView : std::enable_shared_from_this<WidgetView>
{
    // One of a card's two summoner spells; only allies' are timed.
    struct CardSummoner
    {
        Button button;
        Image icon;
        Border shade;
        TextBlock countdown{nullptr};
        hstring spell, name;
        bool timed = false;
        double cooldown = 0;
    };
    struct Card
    {
        Button button;
        Image icon;
        TextBlock champion, player, tier, stats, live, mastery, deathStreak, party;
        Border partyBadge;
        hstring key, imageData, details, masteryDetails, deathStreakText, deathStreakDetails, partyLabel, partyDetails;
        hstring streakBadge, championStats;
        int hero = 0;
        uint32_t partyColor = 0;
        int64_t respawnUntil = 0, revivedAt = 0;
        bool self = false, marked = false, wasDead = false;
        std::array<CardSummoner, 2> summoners;
    };
    Grid root;
    Grid list;
    Grid rosterPanel;
    ScrollViewer rosterScroll;
    TextBlock status = MakeText(L"等待 League 主程序连接…", 12, 0x91acbb);
    TextBlock hint = MakeText(L"Win＋G 打开或关闭此小组件，固定后可保留在游戏上方。", 12, 0xdbc47f);
    TextBlock details = MakeText(L"点击玩家卡片查看已缓存的战绩。", 13);
    std::array<TextBlock, 2> teams{MakeText(L"我方", 16, 0x45c7ce), MakeText(L"对方", 16, 0xf3b37e)};
    std::array<TextBlock, 2> teamSummaries{MakeText(L"", 12, 0x91acbb), MakeText(L"", 12, 0x91acbb)};
    TextBlock liveAlert = MakeText(L"", 13, 0xff8a7a);
    JsonArray liveSections;
    hstring liveRevision;
    TextBlock rosterGuide = MakeText(L"ⓘ", 12, 0x91acbb);
    std::array<Card, 10> cards;
    XboxGameBarWidget widget{nullptr};
    DispatcherTimer timer;
    bool busy = false, stopped = false, enabled = false, active = false;
    int populatedCards = 0;
    bool rosterMarked = false;
    int selectedCard = -1;
    hstring readError;
    hstring revision, command, session, error;
    int64_t lastGood = 0, lastHeartbeat = 0;
    bool capturePending = false;

#include "ChampionDetailView.inl"
#include "DetailCards.inl"
#include "TierChatView.inl"
#include "VoiceChatView.inl"
#include "WaveView.inl"
#include "ControlView.inl"
#include "CompositionView.inl"
#include "RespawnMarkView.inl"
#include "AugmentStackView.inl"
#include "CooldownView.inl"
#include "StatsView.inl"
#include "SummonerTimerView.inl"

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
                fixturePreview ? L"detail-preview-" + detailTab + L".png" : L"preview-verification.png",
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
            Trace(L"preview-captured " + to_hstring(rendered.PixelWidth()) + L"x" + to_hstring(rendered.PixelHeight()));
        }
        catch (hresult_error const &e)
        {
            Trace(L"preview-capture-error " + e.message());
        }
    }

    WidgetView()
    {
        InitializeRoot();
        InitializeRoster();
        InitializeDetails();
        InitializeWaves();
        InitializeControls();
        InitializeComposition();
        InitializeChat();
        InitializeMarks();
    }
    void InitializeRoot()
    {
        root.RequestedTheme(ElementTheme::Dark);
        root.Background(MakeBrush(0x071e29));
        root.Padding({16, 10, 16, 12});
        for (int row = 0; row < 5; ++row)
        {
            RowDefinition def;
            def.Height(row == 2 ? GridLength{1, GridUnitType::Star} : GridLength{1, GridUnitType::Auto});
            root.RowDefinitions().Append(def);
        }
        status.Margin({0, 0, 0, 6});
        root.Children().Append(status);
        hint.TextWrapping(TextWrapping::Wrap);
        hint.Margin({0, 0, 0, 10});
        Grid::SetRow(hint, 1);
        root.Children().Append(hint);
    }
    void InitializeRoster()
    {
        for (int col = 0; col < 2; ++col)
            list.ColumnDefinitions().Append(ColumnDefinition());
        for (int row = 0; row < 5; ++row)
        {
            RowDefinition def;
            def.Height({1, GridUnitType::Star});
            def.MinHeight(64);
            list.RowDefinitions().Append(def);
        }
        for (int i = 0; i < 10; ++i)
        {
            InitializeCard(i);
        }
        rosterScroll.Content(list);
        rosterScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
        rosterScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);
        RowDefinition headerRow;
        headerRow.Height({1, GridUnitType::Auto});
        rosterPanel.RowDefinitions().Append(headerRow);
        RowDefinition alertRow;
        alertRow.Height({1, GridUnitType::Auto});
        rosterPanel.RowDefinitions().Append(alertRow);
        rosterPanel.RowDefinitions().Append(RowDefinition());
        rosterPanel.Children().Append(CreateRosterHeader());
        liveAlert.Margin({0, 0, 0, 6});
        liveAlert.Visibility(Visibility::Collapsed);
        Grid::SetRow(liveAlert, 1);
        rosterPanel.Children().Append(liveAlert);
        Grid::SetRow(rosterScroll, 2);
        rosterPanel.Children().Append(rosterScroll);
        Grid::SetRow(rosterPanel, 2);
        root.Children().Append(rosterPanel);
        details.TextWrapping(TextWrapping::Wrap);
        details.Margin({0, 6, 0, 0});
        details.MinHeight(40);
        Grid::SetRow(details, 3);
        root.Children().Append(details);
    }
    void InitializeCard(int i)
    {
        auto &card = cards[i];
        card.button.Background(MakeBrush(0x112f3c));
        card.button.BorderThickness({0});
        card.button.Padding({10, 6, 10, 6});
        card.button.Margin({0, 0, i < 5 ? 8.0 : 0.0, 6});
        card.button.HorizontalAlignment(HorizontalAlignment::Stretch);
        card.button.VerticalAlignment(VerticalAlignment::Stretch);
        card.button.HorizontalContentAlignment(HorizontalAlignment::Stretch);
        Grid body;
        ColumnDefinition iconCol;
        iconCol.Width({54, GridUnitType::Pixel});
        body.ColumnDefinitions().Append(iconCol);
        body.ColumnDefinitions().Append(ColumnDefinition());
        card.icon.Width(44);
        card.icon.Height(44);
        card.icon.VerticalAlignment(VerticalAlignment::Center);
        body.Children().Append(card.icon);
        StackPanel labels;
        Grid::SetColumn(labels, 1);
        labels.Children().Append(CreateCardHeading(card));
        labels.Children().Append(CreateCardAccount(card));
        // Secondary statistics stay small and muted so champion, tier and player lead the card.
        card.stats = MakeText(L"胜率 —    选用率 —", 11, 0x6aa3ab);
        labels.Children().Append(card.stats);
        body.Children().Append(labels);
        ColumnDefinition summonerColumn;
        summonerColumn.Width({1, GridUnitType::Auto});
        body.ColumnDefinitions().Append(summonerColumn);
        auto summoners = CreateCardSummoners(i);
        Grid::SetColumn(summoners, 2);
        body.Children().Append(summoners);
        card.button.Content(body);
        card.button.Click([this, i](auto &&, auto &&) { OpenDetails(i); });
        Grid::SetRow(card.button, i % 5);
        Grid::SetColumn(card.button, i / 5);
        list.Children().Append(card.button);
    }
    Grid CreateRosterHeader()
    {
        Grid header;
        header.Margin({0, 0, 0, 6});
        for (int col = 0; col < 2; ++col)
            header.ColumnDefinitions().Append(ColumnDefinition());
        ColumnDefinition guideColumn;
        guideColumn.Width({1, GridUnitType::Auto});
        header.ColumnDefinitions().Append(guideColumn);
        for (int i = 0; i < 2; ++i)
        {
            StackPanel team;
            team.Orientation(Orientation::Horizontal);
            teams[i].VerticalAlignment(VerticalAlignment::Center);
            team.Children().Append(teams[i]);
            // Kills, item value and the gold lead from 2999; empty before the game starts.
            teamSummaries[i].VerticalAlignment(VerticalAlignment::Center);
            teamSummaries[i].Margin({8, 2, 0, 0});
            team.Children().Append(teamSummaries[i]);
            Grid::SetColumn(team, i);
            header.Children().Append(team);
        }
        rosterGuide.HorizontalAlignment(HorizontalAlignment::Right);
        rosterGuide.VerticalAlignment(VerticalAlignment::Center);
        rosterGuide.Visibility(Visibility::Collapsed);
        Grid::SetColumn(rosterGuide, 2);
        header.Children().Append(rosterGuide);
        return header;
    }
    // Usage notes move into the header once players load, leaving the height to the ten cards.
    void UpdateRosterGuide()
    {
        bool ready = populatedCards > 0;
        rosterGuide.Text(rosterMarked ? L"同色 = 疑似组队  ⓘ" : L"操作说明  ⓘ");
        ToolTipService::SetToolTip(
            rosterGuide,
            box_value(hstring(L"点击英雄卡片查看技能、出装与模式推荐；详情胜率属于英雄统计。") +
                      (rosterMarked ? L"\n同色与人数标记：根据近期共同战绩推测的疑似组队。" : L"")));
        rosterGuide.Visibility(ready ? Visibility::Visible : Visibility::Collapsed);
        details.Visibility(ready || selectedCard >= 0 ? Visibility::Collapsed : Visibility::Visible);
    }
    Grid CreateCardHeading(Card &card)
    {
        Grid heading;
        ColumnDefinition tierColumn;
        tierColumn.Width({34, GridUnitType::Pixel});
        heading.ColumnDefinitions().Append(tierColumn);
        heading.ColumnDefinitions().Append(ColumnDefinition());
        for (int i = 0; i < 2; ++i)
        {
            ColumnDefinition autoColumn;
            autoColumn.Width({1, GridUnitType::Auto});
            heading.ColumnDefinitions().Append(autoColumn);
        }
        card.tier = MakeText(L"—", 14, 0x91acbb);
        card.tier.Width(34);
        card.champion = MakeText(L"等待英雄", 15);
        heading.Children().Append(card.tier);
        Grid::SetColumn(card.champion, 1);
        heading.Children().Append(card.champion);
        // In game: participation and item value, right-aligned beside the champion name.
        card.live = MakeText(L"", 11, 0x65d6d6);
        card.live.Margin({6, 3, 0, 0});
        card.live.HorizontalAlignment(HorizontalAlignment::Right);
        Grid::SetColumn(card.live, 2);
        heading.Children().Append(card.live);
        card.deathStreak = MakeText(L"", 11, 0xf3b37e);
        card.deathStreak.Margin({6, 3, 0, 0});
        Grid::SetColumn(card.deathStreak, 3);
        heading.Children().Append(card.deathStreak);
        return heading;
    }
    Grid CreateCardAccount(Card &card)
    {
        Grid account;
        ColumnDefinition partyColumn;
        partyColumn.Width({1, GridUnitType::Auto});
        account.ColumnDefinitions().Append(partyColumn);
        account.ColumnDefinitions().Append(ColumnDefinition());
        ColumnDefinition masteryColumn;
        masteryColumn.Width({1, GridUnitType::Auto});
        account.ColumnDefinitions().Append(masteryColumn);
        card.party = MakeText(L"", 10);
        card.partyBadge.Child(card.party);
        card.partyBadge.Padding({4, 0, 4, 0});
        card.partyBadge.CornerRadius({2});
        card.partyBadge.Background(MakeBrush(0x253e4c));
        card.partyBadge.Margin({0, 0, 6, 0});
        card.partyBadge.VerticalAlignment(VerticalAlignment::Center);
        card.partyBadge.Visibility(Visibility::Collapsed);
        account.Children().Append(card.partyBadge);
        card.player = MakeText(L"空缺位置", 12, 0x91acbb);
        Grid::SetColumn(card.player, 1);
        account.Children().Append(card.player);
        card.mastery = MakeText(L"", 11, 0x7f98a4);
        card.mastery.Margin({8, 0, 0, 0});
        Grid::SetColumn(card.mastery, 2);
        account.Children().Append(card.mastery);
        return account;
    }
    void Start(XboxGameBarWidget const &value)
    {
        widget = value;
        if (widget)
        {
            BindWidgetEvents();
        }
        timer.Interval(std::chrono::milliseconds(500));
        timer.Tick(
            [weak = weak_from_this()](auto &&, auto &&)
            {
                if (auto self = weak.lock())
                {
                    self->Read();
                    self->ReadDetails();
                    self->ReadChatStatus();
                    self->ReadVoiceStatus();
                    self->UpdateRespawns();
                    self->UpdateWaves();
                    self->UpdateSummoners();
                }
            });
        timer.Start();
        Read();
        root.Loaded(
            [weak = weak_from_this()](auto &&, auto &&)
            {
                if (auto self = weak.lock())
                    Trace(L"view-loaded " + to_hstring(self->root.ActualWidth()) + L"x" +
                          to_hstring(self->root.ActualHeight()));
            });
    }
    void DispatchWidgetUpdate(std::function<void(WidgetView &)> update)
    {
        root.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal,
                                   [weak = weak_from_this(), update = std::move(update)]
                                   {
                                       if (auto self = weak.lock(); self && !self->stopped && self->widget)
                                       {
                                           update(*self);
                                       }
                                   });
    }
    void BindWidgetEvents()
    {
        widget.GameBarDisplayModeChanged(
            [weak = weak_from_this()](auto &&, auto &&)
            {
                if (auto self = weak.lock())
                {
                    self->DispatchWidgetUpdate(
                        [](WidgetView &view)
                        {
                            if (view.widget.GameBarDisplayMode() == XboxGameBarDisplayMode::PinnedOnly)
                            {
                                view.RequestVoiceStop();
                            }
                            view.SaveChatRequest();
                            view.UpdateChatButtons();
                        });
                }
            });
        widget.PinnedChanged(
            [weak = weak_from_this()](auto &&, auto &&)
            {
                if (auto self = weak.lock())
                {
                    self->DispatchWidgetUpdate(
                        [](WidgetView &view)
                        {
                            view.UpdateHint();
                            Trace(view.widget.Pinned() ? L"widget-pinned" : L"widget-unpinned");
                        });
                }
            });
        widget.VisibleChanged(
            [weak = weak_from_this()](auto &&, auto &&)
            {
                if (auto self = weak.lock())
                {
                    self->DispatchWidgetUpdate(
                        [](WidgetView &view)
                        {
                            if (!view.widget.Visible())
                            {
                                view.RequestVoiceStop();
                            }
                            Trace(view.widget.Visible() ? L"widget-visible" : L"widget-hidden");
                        });
                }
            });
    }
    void Stop()
    {
        CloseVoice();
        ResetChat();
        stopped = true;
        timer.Stop();
        voicePreparingPoll.Stop();
        waveFrames.revoke();
        widget = nullptr;
    }
    void UpdateHint()
    {
        if (stopped)
            return;
        hint.Visibility(Visibility::Visible);
        if (!widget)
        {
            hint.Text(L"桌面预览 · 游戏中请按 Win＋G，打开并固定「League 对局速览」。");
            return;
        }
        if (!error.empty())
        {
            hint.Text(error);
            return;
        }
        if (!widget.Pinned())
            hint.Text(L"尚未固定 · 在 Win＋G 中查看；点击右上角图钉可保留在游戏上方。");
        else
            hint.Visibility(Visibility::Collapsed);
    }
    fire_and_forget SetVisible(bool visible)
    {
        if (stopped || !widget)
        {
            co_return;
        }
        auto lifetime = shared_from_this();
        auto owner = widget;
        try
        {
            if (visible)
                co_await owner.RestoreAsync();
            else
                co_await owner.MinimizeAsync();
        }
        catch (hresult_error const &e)
        {
            error = e.message();
        }
    }
    fire_and_forget SetImage(int index, hstring data)
    {
        auto lifetime = shared_from_this();
        auto &card = cards[index];
        card.imageData = data;
        card.icon.Source(nullptr);
        if (data.empty())
            co_return;
        try
        {
            auto buffer = Windows::Security::Cryptography::CryptographicBuffer::DecodeFromBase64String(data);
            if (buffer.Length() > 100000)
                co_return;
            InMemoryRandomAccessStream stream;
            co_await stream.WriteAsync(buffer);
            stream.Seek(0);
            BitmapImage bitmap;
            bitmap.DecodePixelWidth(48);
            co_await bitmap.SetSourceAsync(stream);
            if (!stopped && card.imageData == data)
            {
                card.icon.Source(bitmap);
                RefreshDetailHeader();
                // The control page copies card icons when it renders; redraw once an icon arrives.
                if (controlOpen)
                    RenderControls();
            }
        }
        catch (...)
        { /* Invalid or unavailable artwork leaves a placeholder, never breaks the roster. */
        }
    }
    void Clear()
    {
        populatedCards = 0;
        rosterMarked = false;
        CloseWaves();
        CloseControls();
        CloseComposition();
        composition = nullptr;
        controlRows = JsonArray();
        ClearMarks();
        ClearAugments();
        ClearSummoners();
        calcCard = -1;
        calcSignature = L"";
        statsSignature = L"";
        CloseDetails(false);
        CloseVoice();
        ResetChat();
        waves = JsonObject();
        waveStops.fill(-1);
        for (auto &card : cards)
        {
            card.hero = 0;
            card.key = L"";
            card.icon.Source(nullptr);
            card.imageData = L"";
            card.details = L"等待当前对局数据。";
            card.champion.Text(L"等待英雄");
            card.player.Text(L"空缺位置");
            card.tier.Text(L"—");
            card.stats.Text(L"胜率 —    选用率 —");
            card.button.IsEnabled(false);
            card.mastery.Text(L"");
            card.masteryDetails = L"";
            card.deathStreak.Text(L"");
            card.deathStreakText = L"";
            card.deathStreakDetails = L"";
            card.party.Text(L"");
            card.partyLabel = L"";
            card.partyDetails = L"";
            card.partyColor = 0;
            card.partyBadge.Visibility(Visibility::Collapsed);
            card.player.Foreground(MakeBrush(0x91acbb));
            card.button.BorderThickness({0});
            card.streakBadge = L"";
            card.championStats = L"";
            card.live.Text(L"");
            card.respawnUntil = 0;
            card.self = false;
            card.icon.Opacity(1);
        }
        for (auto &summary : teamSummaries)
            summary.Text(L"");
        liveSections = JsonArray();
        liveRevision = L"";
        liveAlert.Visibility(Visibility::Collapsed);
        details.Text(L"等待当前对局数据。");
        UpdateRosterGuide();
    }
    void ValidateSnapshot(JsonObject const &data, hstring &connectionMessage)
    {
        double age = fixturePreview ? 0 : Now() - data.GetNamedNumber(L"updatedAt", 0);
        if (data.GetNamedNumber(L"protocol", 0) != 1)
        {
            connectionMessage = L"主程序与组件版本不兼容，请更新对局速览组件。";
            throw hresult_invalid_argument();
        }
        if (age < 0 || age > 15000)
        {
            connectionMessage = L"主程序数据已超过 15 秒未更新；请确认 League 正在运行，且对局速览已选择 Game Bar。";
            throw hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT), connectionMessage);
        }
    }
    void ApplySnapshot(JsonObject const &data)
    {
        bool previouslyActive = active;
        // Re-reading an old file must not extend its connection lifetime.
        lastGood = fixturePreview ? Now() : static_cast<int64_t>(data.GetNamedNumber(L"updatedAt", 0));
        enabled = data.GetNamedBoolean(L"enabled", false);
        active = data.GetNamedBoolean(L"active", false);
        supportsDetails = data.GetNamedNumber(L"detailProtocol", 0) == 1;
        supportsChat = data.GetNamedNumber(L"chatProtocol", 0) == 1;
        supportsMasteryChat = data.GetNamedBoolean(L"chatMastery", false);
        supportsDeathStreakChat = data.GetNamedBoolean(L"chatDeathStreak", false);
        supportsRecordChat = data.GetNamedBoolean(L"chatRecord", false);
        supportsPartyChat = data.GetNamedBoolean(L"chatParty", false);
        supportsControlChat = data.GetNamedBoolean(L"chatControls", false);
        supportsHardControlChat = data.GetNamedBoolean(L"chatHardControls", false);
        supportsWaveChat = data.GetNamedBoolean(L"chatWaves", false);
        chatTeams = data.GetNamedBoolean(L"chatTeams", false);
        supportsVoiceChat = data.GetNamedBoolean(L"chatVoice", false);
        supportsSummonerChat = data.GetNamedBoolean(L"chatSummoner", false);
        supportsAugmentChat = data.GetNamedBoolean(L"chatAugments", false);
        supportsVoiceRecording = data.GetNamedNumber(L"voiceProtocol", 0) == 2;
        if (!enabled || !active)
            CloseVoice();
        detailQueue = static_cast<int>(data.GetNamedNumber(L"queue", 0));
        auto next = data.GetNamedString(L"revision", L"");
        if (next != revision)
        {
            revision = next;
            auto nextSession = data.GetNamedString(L"session", L"");
            if (session != nextSession)
            {
                Clear();
                session = nextSession;
            }
            ApplyRoster(data);
            ApplyVisibilityCommand(data, previouslyActive);
        }
    }
    void ApplyRoster(JsonObject const &data)
    {
        status.Text(
            data.GetNamedString(L"status", active ? L"读取当前对局…" : L"暂无进行中的对局，等待进入加载界面。"));
        UpdateHint();
        auto teamNames = data.GetNamedArray(L"teams", JsonArray());
        for (uint32_t i = 0; i < 2 && i < teamNames.Size(); ++i)
            teams[i].Text(teamNames.GetStringAt(i));
        ApplyLive(data);
        ApplyWaves(data);
        ApplyAugments(data);
        ApplySummonerData(data);
        auto rows = data.GetNamedArray(L"players", JsonArray());
        populatedCards = 0;
        bool marked = false;
        for (uint32_t i = 0; i < 10; ++i)
        {
            UpdateCard(i, i < rows.Size() ? rows.GetObjectAt(i) : JsonObject());
            if (cards[i].button.IsEnabled())
            {
                ++populatedCards;
            }
            marked = marked || cards[i].partyColor != 0;
        }
        rosterMarked = marked;
        ApplyControls(data, rows);
        ApplyComposition(data);
        ApplyCalculator();
        UpdateRespawns();
        if (selectedCard >= 0)
            RefreshDetailHeader();
        UpdateRosterGuide();
        if (fixturePreview && !fixtureOpened && populatedCards > 0)
        {
            fixtureOpened = true;
            if (detailTab == L"voice")
                ConfigureVoicePreview();
            else if (detailTab == L"waves")
                OpenWaves();
            else if (detailTab == L"controls")
                OpenControls();
            else if (detailTab == L"marks")
            {
                ToggleMark(6);
                ToggleMark(8);
            }
            else if (detailTab != L"roster")
                OpenDetails(0);
        }
    }
    void UpdateCard(uint32_t i, JsonObject const &row)
    {
        auto &card = cards[i];
        card.champion.Text(row.GetNamedString(L"champion", L"等待英雄"));
        int hero = static_cast<int>(row.GetNamedNumber(L"championId", 0));
        if (selectedCard == static_cast<int>(i) && card.hero != hero)
            CloseDetails(false);
        ForgetMarkIfReplaced(card, hero);
        card.hero = hero;
        card.key = row.GetNamedString(L"key", L"");
        auto playerName = row.GetNamedString(L"name", L"空缺位置");
        card.player.Text(playerName.empty() ? L"玩家名称尚未提供" : playerName);
        card.tier.Text(row.GetNamedString(L"tier", L"—"));
        int partySize = static_cast<int>(row.GetNamedNumber(L"partySize", 0));
        card.partyColor = partySize >= 2 && partySize <= 5
                              ? static_cast<uint32_t>(row.GetNamedNumber(L"partyColor", 0)) & 0xffffff
                              : 0;
        card.partyLabel = card.partyColor ? row.GetNamedString(L"partyLabel", L"") : L"";
        card.partyDetails = card.partyColor ? row.GetNamedString(L"partyDetails", L"") : L"";
        card.party.Text(card.partyColor ? row.GetNamedString(L"party", L"") : L"");
        card.party.Foreground(MakeBrush(card.partyColor));
        card.partyBadge.Visibility(card.partyColor ? Visibility::Visible : Visibility::Collapsed);
        card.player.Foreground(MakeBrush(card.partyColor ? card.partyColor : 0x91acbb));
        card.button.BorderBrush(MakeBrush(card.partyColor ? card.partyColor : 0x315662));
        card.button.BorderThickness({card.partyColor ? 3.0 : 0.0, 0, 0, 0});
        ToolTip partyTip;
        auto partyText = MakeText(card.partyDetails, 12);
        partyText.TextWrapping(TextWrapping::Wrap);
        partyText.MaxWidth(320);
        partyTip.Content(partyText);
        ToolTipService::SetToolTip(card.partyBadge, partyTip);
        ToolTipService::SetToolTip(
            card.player, box_value(playerName + (card.partyDetails.empty() ? L"" : L"\n" + card.partyDetails)));
        card.tier.Foreground(MakeBrush(static_cast<uint32_t>(row.GetNamedNumber(L"color", 0x91acbb))));
        card.mastery.Text(row.GetNamedString(L"mastery", L""));
        card.masteryDetails = row.GetNamedString(L"masteryDetails", L"");
        ToolTipService::SetToolTip(card.mastery, box_value(card.masteryDetails));
        card.deathStreakText = row.GetNamedString(L"deathStreak", L"");
        card.deathStreakDetails = row.GetNamedString(L"deathStreakDetails", L"");
        // Only a run of deaths is worth a glance on the card; the full record stays in the tooltip and detail.
        auto current = row.TryLookup(L"currentDeathStreak");
        int streak =
            current && current.ValueType() == JsonValueType::Number ? static_cast<int>(current.GetNumber()) : 0;
        card.streakBadge = streak >= 2 ? L"连死 " + to_hstring(streak) : L"";
        ToolTipService::SetToolTip(card.deathStreak, box_value(card.deathStreakDetails));
        card.championStats = row.GetNamedString(L"stats", L"胜率 —    选用率 —");
        card.stats.Text(card.championStats);
        ToolTipService::SetToolTip(
            card.stats,
            box_value(card.deathStreakText +
                      (card.deathStreakDetails.empty() ? L"" : L"\n" + card.deathStreakDetails)));
        card.live.Text(row.GetNamedString(L"liveStats", L""));
        card.self = row.GetNamedBoolean(L"self", false);
        double respawn = row.GetNamedNumber(L"respawn", 0);
        card.respawnUntil = respawn > 0 ? lastGood + static_cast<int64_t>(respawn * 1000) : 0;
        RenderRespawn(card);
        card.details = row.GetNamedString(L"details", L"等待当前对局数据。");
        card.button.IsEnabled(row.HasKey(L"name"));
        // Image decoding must never block the other nine players or the connection heartbeat.
        auto image = row.GetNamedString(L"icon", L"");
        if (card.imageData != image)
            SetImage(i, image);
        UpdateCardSummoners(card, row);
    }
    void ApplyLive(JsonObject const &data)
    {
        auto summaries = data.GetNamedArray(L"teamSummaries", JsonArray());
        for (uint32_t i = 0; i < 2; ++i)
            teamSummaries[i].Text(i < summaries.Size() ? summaries.GetStringAt(i) : L"");
        auto next = data.GetNamedString(L"liveRevision", L"");
        liveSections = data.GetNamedArray(L"liveSections", JsonArray());
        if (next == liveRevision)
            return;
        liveRevision = next;
        if (selectedCard >= 0)
        {
            RefreshDetailHeader();
            if (detailTab == L"live")
                RenderDetails(false);
        }
    }
    static int RespawnSeconds(Card const &card)
    {
        auto left = card.respawnUntil - Now();
        return left > 0 ? static_cast<int>((left + 999) / 1000) : 0;
    }
    // A dead player's icon dims and the heading counts down; the death-streak badge returns on respawn.
    static void RenderRespawn(Card &card)
    {
        int seconds = RespawnSeconds(card);
        card.icon.Opacity(seconds > 0 ? .35 : 1);
        card.deathStreak.Text(seconds > 0 ? L"复活 " + to_hstring(seconds) : card.streakBadge);
        card.deathStreak.Foreground(MakeBrush(seconds > 0 ? 0xff8a7a : 0xf3b37e));
    }
    void UpdateRespawns()
    {
        if (stopped)
            return;
        std::wstring enemies;
        int allies = 0;
        for (int i = 0; i < 10; ++i)
        {
            RenderRespawn(cards[i]);
            int seconds = cards[i].hero > 0 ? RespawnSeconds(cards[i]) : 0;
            if (seconds <= 0)
                continue;
            if (i < 5)
                ++allies;
            else
                enemies += L" · " + std::wstring(cards[i].champion.Text()) + L" " + std::to_wstring(seconds) + L" 秒";
        }
        std::wstring text = enemies.empty() ? L"" : std::wstring(teams[1].Text()) + L"阵亡" + enemies;
        if (allies > 0)
            text += (text.empty() ? L"" : L"    ") + std::wstring(teams[0].Text()) + L"阵亡 " +
                    std::to_wstring(allies) + L" 人";
        liveAlert.Text(text);
        liveAlert.Visibility(text.empty() ? Visibility::Collapsed : Visibility::Visible);
        UpdateMarks();
    }
    void ApplyVisibilityCommand(JsonObject const &data, bool previouslyActive)
    {
        auto nextCommand = data.GetNamedString(L"command", L"");
        if (widget && command != nextCommand)
        {
            bool firstCommand = command.empty();
            command = nextCommand;
            if (enabled && active && data.GetNamedBoolean(L"show", false))
                SetVisible(true);
            else if (!firstCommand && (previouslyActive || !enabled))
                SetVisible(false);
        }
    }
    IAsyncAction WriteHeartbeat()
    {
        if (stopped || Now() - lastHeartbeat <= 5000)
        {
            co_return;
        }
        auto lifetime = shared_from_this();
        lastHeartbeat = Now();
        try
        {
            JsonObject state;
            state.Insert(L"protocol", JsonValue::CreateNumberValue(1));
            state.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(Now())));
            state.Insert(L"pinned", JsonValue::CreateBooleanValue(widget && widget.Pinned()));
            state.Insert(L"windowState", JsonValue::CreateNumberValue(widget ? int(widget.WindowState()) : -1));
            state.Insert(L"error", JsonValue::CreateStringValue(error));
            state.Insert(L"readError", JsonValue::CreateStringValue(readError));
            state.Insert(L"connected", JsonValue::CreateBooleanValue(lastGood > 0 && Now() - lastGood <= 15000));
            state.Insert(L"active", JsonValue::CreateBooleanValue(active));
            state.Insert(L"revision", JsonValue::CreateStringValue(revision));
            state.Insert(L"players", JsonValue::CreateNumberValue(populatedCards));
            state.Insert(L"width", JsonValue::CreateNumberValue(root.ActualWidth()));
            state.Insert(L"height", JsonValue::CreateNumberValue(root.ActualHeight()));
            state.Insert(L"visible", JsonValue::CreateBooleanValue(widget ? widget.Visible() : root.IsLoaded()));
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                widget ? L"widget-status.json" : L"preview-status.json", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, state.Stringify());
        }
        catch (...)
        {
            // A heartbeat is diagnostic only; transient storage failures do not disconnect the roster.
        }
    }
    void CaptureWhenReady()
    {
        if (capturePending && root.ActualWidth() > 0 && root.ActualHeight() > 0 && populatedCards > 0 &&
            (!fixturePreview || detailTab == L"roster" || detailTab == L"voice" || detailTab == L"waves" ||
             detailTab == L"marks" || (detailTab == L"controls" && ControlIconsReady()) ||
             (!detailRevision.empty() && pendingDetailImages == 0)))
        {
            capturePending = false;
            CapturePreview();
        }
    }
    fire_and_forget Read()
    {
        if (busy || stopped)
        {
            co_return;
        }
        auto lifetime = shared_from_this();
        busy = true;
        hstring connectionMessage = L"尚未收到主程序数据；请启动 League，并在对局速览设置中选择 Game Bar。";
        try
        {
            auto data =
                co_await ReadLocalSnapshotAsync(fixturePreview ? L"detail-preview-snapshot.json" : L"snapshot.json");
            if (!stopped)
            {
                if (fixturePreview)
                {
                    root.Width(std::clamp(data.GetNamedNumber(L"width", 820), 640.0, 1100.0));
                    root.Height(std::clamp(data.GetNamedNumber(L"height", 560), 500.0, 800.0));
                }
                ValidateSnapshot(data, connectionMessage);
                if (!readError.empty())
                {
                    Trace(L"snapshot-read-recovered");
                }
                readError = L"";
                ApplySnapshot(data);
            }
        }
        catch (hresult_error const &exception)
        {
            hstring error = to_hstring(uint32_t(exception.code())) + L" " + exception.message();
            // Logged once per distinct error; the heartbeat only keeps the latest one.
            if (error != readError)
            {
                Trace(L"snapshot-read-error " + error);
            }
            readError = error;
            if (Now() - lastGood > 15000 && !stopped)
            {
                active = false;
                revision = L"";
                Clear();
                populatedCards = 0;
                status.Text(connectionMessage);
                details.Text(L"连接恢复后会自动更新，无需重新打开组件。");
            }
        }
        if (!stopped)
        {
            co_await WriteHeartbeat();
        }
        busy = false;
        if (!stopped)
        {
            UpdateChatButtons();
            CaptureWhenReady();
        }
    }
};

struct WidgetApp : ApplicationT<WidgetApp>
{
    // Must match the second AppExtension Id in Package.appxmanifest.
    static constexpr wchar_t const *WaveMiniExtensionId = L"Waves";
    static constexpr std::wstring_view WaveMiniPreviewPrefix = L"--capture-wave-mini:";
    std::vector<std::shared_ptr<WidgetView>> views;
    std::vector<std::shared_ptr<WaveMiniView>> miniViews;
    WidgetApp()
    {
        UnhandledException([](auto &&, UnhandledExceptionEventArgs const &args) { LogError(args.Message()); });
        Suspending(
            [this](auto &&, auto &&)
            {
                for (auto const &view : views)
                    if (!view->stopped)
                        view->CloseVoice();
            });
    }
    void OnActivated(IActivatedEventArgs const &args)
    {
        Trace(L"activation kind=" + to_hstring(int(args.Kind())) + L" version=" + PackageVersion());
        auto activation = args.try_as<XboxGameBarWidgetActivatedEventArgs>();
        if (!activation)
        {
            Trace(L"activation-not-widget");
            return;
        }
        if (!activation.IsLaunchActivation())
        {
            Trace(L"widget-repeat-activation");
            return;
        }
        Trace(L"widget-initial-activation id=" + activation.AppExtensionId());
        Frame frame;
        auto window = Window::Current();
        frame.HorizontalContentAlignment(HorizontalAlignment::Stretch);
        frame.VerticalContentAlignment(VerticalAlignment::Stretch);
        window.Content(frame);
        auto widget = XboxGameBarWidget(activation, window.CoreWindow(), frame);
        if (activation.AppExtensionId() == WaveMiniExtensionId)
        {
            StartWaveMini(window, frame, widget);
            return;
        }
        auto view = std::make_shared<WidgetView>();
        Page page;
        page.Content(view->root);
        frame.Content(page);
        view->Start(widget);
        std::erase_if(views, [](auto const &item) { return item->stopped; });
        views.push_back(view);
        window.Closed(
            [weak = std::weak_ptr<WidgetView>(view)](auto &&, auto &&)
            {
                if (auto value = weak.lock())
                    value->Stop();
            });
        window.Activate();
        Trace(L"widget-window-activated");
    }
    // Game Bar composites the widget over the game, so the frame and page stay transparent and the view's own
    // background carries the chosen opacity.
    void StartWaveMini(Window const &window, Frame const &frame, XboxGameBarWidget const &widget)
    {
        SolidColorBrush clear(Colors::Transparent());
        frame.Background(clear);
        Page page;
        page.Background(clear);
        auto view = std::make_shared<WaveMiniView>();
        page.Content(view->root);
        frame.Content(page);
        view->Start(widget);
        TrackWaveMini(window, view);
        window.Activate();
        Trace(L"wave-mini-window-activated");
    }
    // Renders the mini widget from the detail fixture with a frozen clock and saves a PNG; replaces any desktop
    // preview in this window.
    void ShowWaveMiniPreview(Window const &window, hstring const &arguments)
    {
        for (auto const &view : views)
            if (!view->widget)
                view->Stop();
        for (auto const &view : miniViews)
            if (!view->widget)
                view->Stop();
        auto view = std::make_shared<WaveMiniView>();
        view->ConfigurePreview(arguments);
        Page page;
        page.Content(view->root);
        window.Content(page);
        view->Start(nullptr);
        TrackWaveMini(window, view);
    }
    void TrackWaveMini(Window const &window, std::shared_ptr<WaveMiniView> const &view)
    {
        std::erase_if(miniViews, [](auto const &item) { return item->stopped; });
        miniViews.push_back(view);
        window.Closed(
            [weak = std::weak_ptr<WaveMiniView>(view)](auto &&, auto &&)
            {
                if (auto value = weak.lock())
                    value->Stop();
            });
    }
    void OnLaunched(LaunchActivatedEventArgs const &args)
    {
        // Use the identical renderer for a real desktop preview, without registering game shortcuts.
        Trace(L"desktop-preview-launch");
        auto window = Window::Current();
        if (std::wstring_view(args.Arguments()).starts_with(WaveMiniPreviewPrefix))
        {
            ShowWaveMiniPreview(window, args.Arguments());
        }
        // A mini preview may have replaced the roster preview; a later roster preview starts a fresh one.
        else if (std::none_of(
                     views.begin(), views.end(), [](auto const &view) { return !view->widget && !view->stopped; }))
        {
            auto view = std::make_shared<WidgetView>();
            Page page;
            page.Content(view->root);
            window.Content(page);
            view->capturePending = args.Arguments() == L"--capture-preview";
            view->ConfigureDetailPreview(args.Arguments());
            view->Start(nullptr);
            std::erase_if(views, [](auto const &item) { return item->stopped; });
            views.push_back(view);
            window.Closed(
                [weak = std::weak_ptr<WidgetView>(view)](auto &&, auto &&)
                {
                    if (auto value = weak.lock())
                        value->Stop();
                });
        }
        else if (args.Arguments() == L"--capture-preview")
        {
            for (auto const &view : views)
                if (!view->widget && !view->stopped)
                    view->CapturePreview();
        }
        else
        {
            for (auto const &view : views)
                if (!view->widget && !view->stopped)
                {
                    view->ConfigureDetailPreview(args.Arguments());
                    view->Read();
                }
        }
        window.Activate();
    }
};

int __stdcall wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    try
    {
        // UWP starts from MTA; Application::Start creates the UI apartment itself.
        init_apartment(apartment_type::multi_threaded);
        Application::Start([](auto &&) { make<WidgetApp>(); });
    }
    catch (hresult_error const &e)
    {
        LogError(to_hstring(uint32_t(e.code())) + L" " + e.message());
        return 1;
    }
    return 0;
}
