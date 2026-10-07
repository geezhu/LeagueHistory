// Game Bar owns this window. Never activate or reposition the desktop League window here.
#include <windows.h>
#include <winrt/Windows.ApplicationModel.Activation.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Security.Cryptography.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.FileProperties.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Microsoft.Gaming.XboxGameBar.h>
#include <array>
#include <chrono>
#include <memory>
#include <fstream>
#include <vector>

using namespace winrt;
using namespace Windows::ApplicationModel::Activation;
using namespace Windows::Foundation;
using namespace Windows::Data::Json;
using namespace Windows::Storage;
using namespace Windows::Storage::Streams;
using namespace Windows::UI;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;
using namespace Windows::UI::Xaml::Media::Imaging;
using namespace Microsoft::Gaming::XboxGameBar;

static SolidColorBrush MakeBrush(uint32_t rgb)
{
    return SolidColorBrush(Color{255, uint8_t(rgb >> 16), uint8_t(rgb >> 8), uint8_t(rgb)});
}
static TextBlock MakeText(hstring const& value, double size = 14, uint32_t color = 0xe2eef3)
{
    TextBlock text;
    text.Text(value); text.FontSize(size); text.Foreground(MakeBrush(color));
    text.TextTrimming(TextTrimming::CharacterEllipsis);
    return text;
}
static int64_t Now()
{
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::system_clock::now().time_since_epoch()).count();
}
static void LogError(hstring const& message) noexcept
{
    try
    {
        auto path = ApplicationData::Current().LocalFolder().Path() + L"\\component-error.txt";
        std::ofstream log(path.c_str(), std::ios::app);
        log << Now() << " " << to_string(message) << "\n";
    }
    catch (...) {}
}
static void Trace(hstring const& message) noexcept
{
    try
    {
        auto path = ApplicationData::Current().LocalFolder().Path() + L"\\component-diagnostics.txt";
        std::ofstream log(path.c_str(), std::ios::app);
        if (log.tellp() > 65536) { log.close(); log.open(path.c_str(), std::ios::trunc); }
        log << Now() << " " << to_string(message) << "\n";
    }
    catch (...) {}
}

struct WidgetView : std::enable_shared_from_this<WidgetView>
{
    struct Card
    {
        Button button;
        Image icon;
        TextBlock champion, player, tier, stats, mastery, deathStreak, party;
        Border partyBadge;
        hstring imageData, details, masteryDetails, deathStreakDetails, partyLabel, partyDetails;
        int hero = 0;
        uint32_t partyColor = 0;
    };
    Grid root;
    Grid list;
    ScrollViewer rosterScroll;
    TextBlock status = MakeText(L"等待 League 主程序连接…", 12, 0x91acbb);
    TextBlock hint = MakeText(L"Win＋G 打开或关闭此小组件，固定后可保留在游戏上方。", 12, 0xdbc47f);
    TextBlock details = MakeText(L"点击玩家卡片查看已缓存的战绩。", 13);
    std::array<TextBlock, 2> teams{MakeText(L"我方", 16, 0x45c7ce), MakeText(L"对方", 16, 0xf3b37e)};
    std::array<Card, 10> cards;
    XboxGameBarWidget widget{nullptr};
    DispatcherTimer timer;
    bool busy = false, stopped = false, enabled = false, active = false;
    int populatedCards = 0;
    int selectedCard = -1;
    hstring readError;
    hstring revision, command, session, error;
    int64_t lastGood = 0, lastHeartbeat = 0;
    bool capturePending = false;

#include "ChampionDetailView.inl"
#include "TierChatView.inl"

    fire_and_forget CapturePreview()
    {
        auto lifetime = shared_from_this();
        try
        {
            using namespace Windows::Graphics::Imaging;
            RenderTargetBitmap rendered; co_await rendered.RenderAsync(root);
            auto buffer = co_await rendered.GetPixelsAsync();
            std::vector<uint8_t> pixels(buffer.Length()); DataReader::FromBuffer(buffer).ReadBytes(pixels);
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(fixturePreview?L"detail-preview-"+detailTab+L".png":L"preview-verification.png", CreationCollisionOption::ReplaceExisting);
            auto stream = co_await file.OpenAsync(FileAccessMode::ReadWrite);
            auto encoder = co_await BitmapEncoder::CreateAsync(BitmapEncoder::PngEncoderId(), stream);
            encoder.SetPixelData(BitmapPixelFormat::Bgra8, BitmapAlphaMode::Premultiplied, rendered.PixelWidth(), rendered.PixelHeight(), 96, 96, pixels);
            co_await encoder.FlushAsync();
            Trace(L"preview-captured " + to_hstring(rendered.PixelWidth()) + L"x" + to_hstring(rendered.PixelHeight()));
        }
        catch (hresult_error const& e) { Trace(L"preview-capture-error " + e.message()); }
    }

    WidgetView()
    {
        root.RequestedTheme(ElementTheme::Dark);
        root.Background(MakeBrush(0x071e29)); root.Padding({16, 10, 16, 12});
        for (int row = 0; row < 5; ++row)
        {
            RowDefinition def;
            def.Height(row == 2 ? GridLength{1, GridUnitType::Star} : GridLength{1, GridUnitType::Auto});
            root.RowDefinitions().Append(def);
        }
        status.Margin({0, 0, 0, 6}); root.Children().Append(status);
        hint.TextWrapping(TextWrapping::Wrap); hint.Margin({0, 0, 0, 10});
        Grid::SetRow(hint, 1); root.Children().Append(hint);
        for (int col = 0; col < 2; ++col) list.ColumnDefinitions().Append(ColumnDefinition());
        for (int row = 0; row < 6; ++row)
        {
            RowDefinition def; def.Height({row == 0 ? 30.0 : 1.0, row == 0 ? GridUnitType::Pixel : GridUnitType::Star});
            if(row>0)def.MinHeight(64);
            list.RowDefinitions().Append(def);
        }
        for (int i = 0; i < 2; ++i) { Grid::SetColumn(teams[i], i); list.Children().Append(teams[i]); }
        for (int i = 0; i < 10; ++i)
        {
            auto& card = cards[i];
            card.button.Background(MakeBrush(0x112f3c)); card.button.BorderThickness({0});
            card.button.Padding({10, 6, 10, 6}); card.button.Margin({0, 0, i < 5 ? 8.0 : 0.0, 6});
            card.button.HorizontalAlignment(HorizontalAlignment::Stretch);
            card.button.VerticalAlignment(VerticalAlignment::Stretch);
            card.button.HorizontalContentAlignment(HorizontalAlignment::Stretch);
            Grid body; ColumnDefinition iconCol; iconCol.Width({54, GridUnitType::Pixel});
            body.ColumnDefinitions().Append(iconCol); body.ColumnDefinitions().Append(ColumnDefinition());
            card.icon.Width(44); card.icon.Height(44); card.icon.VerticalAlignment(VerticalAlignment::Center);
            body.Children().Append(card.icon);
            StackPanel labels;
            Grid::SetColumn(labels, 1);
            Grid heading;
            ColumnDefinition tierColumn;tierColumn.Width({34,GridUnitType::Pixel});heading.ColumnDefinitions().Append(tierColumn);
            heading.ColumnDefinitions().Append(ColumnDefinition());
            ColumnDefinition streakColumn;streakColumn.Width({1,GridUnitType::Auto});heading.ColumnDefinitions().Append(streakColumn);
            card.tier = MakeText(L"—", 14, 0x91acbb); card.tier.Width(34);
            card.champion = MakeText(L"等待英雄", 15);
            heading.Children().Append(card.tier);Grid::SetColumn(card.champion,1);heading.Children().Append(card.champion);
            card.deathStreak=MakeText(L"",11,0xf3b37e);card.deathStreak.Margin({6,3,0,0});Grid::SetColumn(card.deathStreak,2);heading.Children().Append(card.deathStreak);
            labels.Children().Append(heading);
            Grid account;
            ColumnDefinition partyColumn;partyColumn.Width({1,GridUnitType::Auto});account.ColumnDefinitions().Append(partyColumn);
            account.ColumnDefinitions().Append(ColumnDefinition());
            ColumnDefinition masteryColumn;masteryColumn.Width({1,GridUnitType::Auto});account.ColumnDefinitions().Append(masteryColumn);
            card.party=MakeText(L"",10);card.partyBadge.Child(card.party);card.partyBadge.Padding({4,0,4,0});card.partyBadge.CornerRadius({2});card.partyBadge.Background(MakeBrush(0x253e4c));
            card.partyBadge.Margin({0,0,6,0});card.partyBadge.VerticalAlignment(VerticalAlignment::Center);card.partyBadge.Visibility(Visibility::Collapsed);account.Children().Append(card.partyBadge);
            card.player = MakeText(L"空缺位置", 12, 0x91acbb);Grid::SetColumn(card.player,1);account.Children().Append(card.player);
            card.mastery = MakeText(L"", 12, 0xdbc47f);card.mastery.Margin({8,0,0,0});Grid::SetColumn(card.mastery,2);account.Children().Append(card.mastery);
            labels.Children().Append(account);
            card.stats = MakeText(L"胜率 —    选用率 —", 12, 0x45c7ce); labels.Children().Append(card.stats);
            body.Children().Append(labels); card.button.Content(body);
            card.button.Click([this, i](auto&&, auto&&) { OpenDetails(i); });
            Grid::SetRow(card.button, i % 5 + 1); Grid::SetColumn(card.button, i / 5);
            list.Children().Append(card.button);
        }
        rosterScroll.Content(list);rosterScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
        rosterScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto);Grid::SetRow(rosterScroll,2);root.Children().Append(rosterScroll);
        details.TextWrapping(TextWrapping::Wrap); details.Margin({0, 6, 0, 0}); details.MinHeight(40);
        Grid::SetRow(details, 3); root.Children().Append(details);
        InitializeDetails();
        InitializeChat();
    }
    void Start(XboxGameBarWidget const& value)
    {
        widget = value;
        if (widget)
        {
            widget.GameBarDisplayModeChanged([weak=weak_from_this()](auto&&,auto&&){
                if(auto self=weak.lock())self->root.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal,[self]{
                    if(!self->stopped)self->SaveChatRequest();
                });
            });
            widget.PinnedChanged([weak = weak_from_this()](auto&&, auto&&) {
                if (auto self = weak.lock()) self->root.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Normal, [self] {
                    if (self->stopped || !self->widget) return;
                    self->UpdateHint(); Trace(self->widget.Pinned() ? L"widget-pinned" : L"widget-unpinned");
                });
            });
            widget.VisibleChanged([weak = weak_from_this()](auto&&, auto&&) {
                if (auto self = weak.lock(); self && !self->stopped && self->widget) Trace(self->widget.Visible() ? L"widget-visible" : L"widget-hidden");
            });
        }
        timer.Interval(std::chrono::milliseconds(500));
        timer.Tick([weak = weak_from_this()](auto&&, auto&&) { if (auto self = weak.lock()) { self->Read(); self->ReadDetails(); self->ReadChatStatus(); } });
        timer.Start(); Read();
        root.Loaded([weak = weak_from_this()](auto&&, auto&&) {
            if (auto self = weak.lock()) Trace(L"view-loaded " + to_hstring(self->root.ActualWidth()) + L"x" + to_hstring(self->root.ActualHeight()));
        });
    }
    void Stop()
    {
        stopped = true; timer.Stop(); widget = nullptr;
    }
    void UpdateHint()
    {
        if (stopped) return;
        if (!widget) { hint.Text(L"桌面预览 · 游戏中请按 Win＋G，打开并固定「League 对局速览」。"); return; }
        if (!error.empty()) { hint.Text(error); return; }
        if (!widget.Pinned())
            hint.Text(L"尚未固定 · 在 Win＋G 中查看；点击右上角图钉可保留在游戏上方。");
        else hint.Text(L"已固定 · Win＋G 管理显示或关闭 · 点击卡片查看英雄详情。");
    }
    fire_and_forget SetVisible(bool visible)
    {
        auto lifetime = shared_from_this();
        try { if (visible) co_await widget.RestoreAsync(); else co_await widget.MinimizeAsync(); }
        catch (hresult_error const& e) { error = e.message(); }
    }
    fire_and_forget SetImage(int index, hstring data)
    {
        auto lifetime = shared_from_this();
        auto& card = cards[index]; card.imageData = data; card.icon.Source(nullptr);
        if (data.empty()) co_return;
        try
        {
            auto buffer = Windows::Security::Cryptography::CryptographicBuffer::DecodeFromBase64String(data);
            if (buffer.Length() > 100000) co_return;
            InMemoryRandomAccessStream stream; co_await stream.WriteAsync(buffer); stream.Seek(0);
            BitmapImage bitmap; bitmap.DecodePixelWidth(48); co_await bitmap.SetSourceAsync(stream);
            if (!stopped && card.imageData == data) { card.icon.Source(bitmap); RefreshDetailHeader(); }
        }
        catch (...) { /* Invalid or unavailable artwork leaves a placeholder, never breaks the roster. */ }
    }
    void Clear()
    {
        CloseDetails(false);
        ResetChat();
        for (auto& card : cards)
        {
            card.hero=0; card.icon.Source(nullptr); card.imageData = L""; card.details = L"等待当前对局数据。";
            card.champion.Text(L"等待英雄"); card.player.Text(L"空缺位置"); card.tier.Text(L"—");
            card.stats.Text(L"胜率 —    选用率 —"); card.button.IsEnabled(false);
            card.mastery.Text(L""); card.masteryDetails=L"";card.deathStreak.Text(L"");card.deathStreakDetails=L"";
            card.party.Text(L"");card.partyLabel=L"";card.partyDetails=L"";card.partyColor=0;card.partyBadge.Visibility(Visibility::Collapsed);
            card.player.Foreground(MakeBrush(0x91acbb));card.button.BorderThickness({0});
        }
        details.Text(L"等待当前对局数据。");
    }
    fire_and_forget Read()
    {
        if (busy || stopped) co_return;
        auto lifetime = shared_from_this(); busy = true;
        hstring connectionMessage = L"尚未收到主程序数据；请启动 League，并在对局速览设置中选择 Game Bar。";
        try
        {
            auto folder = ApplicationData::Current().LocalFolder();
            auto file = co_await folder.GetFileAsync(fixturePreview?L"detail-preview-snapshot.json":L"snapshot.json");
            auto properties = co_await file.GetBasicPropertiesAsync();
            if (properties.Size() > 2000000) throw hresult_invalid_argument();
            auto contents = co_await FileIO::ReadTextAsync(file);
            if (stopped) { busy = false; co_return; }
            auto data = JsonObject::Parse(contents);
            if(fixturePreview){root.Width(std::clamp(data.GetNamedNumber(L"width",820),640.0,1100.0));root.Height(std::clamp(data.GetNamedNumber(L"height",560),500.0,800.0));}
            readError = L"";
            double age = fixturePreview?0:Now() - data.GetNamedNumber(L"updatedAt", 0);
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
            bool previouslyActive = active;
            // Re-reading an old file must not extend its connection lifetime.
            lastGood = fixturePreview?Now():static_cast<int64_t>(data.GetNamedNumber(L"updatedAt", 0));
            enabled = data.GetNamedBoolean(L"enabled", false); active = data.GetNamedBoolean(L"active", false);
            supportsDetails = data.GetNamedNumber(L"detailProtocol",0)==1;
            supportsChat=data.GetNamedNumber(L"chatProtocol",0)==1;supportsMasteryChat=data.GetNamedBoolean(L"chatMastery",false);supportsDeathStreakChat=data.GetNamedBoolean(L"chatDeathStreak",false);chatTeams=data.GetNamedBoolean(L"chatTeams",false);
            detailQueue = static_cast<int>(data.GetNamedNumber(L"queue",0));
            auto next = data.GetNamedString(L"revision", L"");
            if (next != revision)
            {
                revision = next;
                auto nextSession = data.GetNamedString(L"session", L"");
                if (session != nextSession) { Clear(); session = nextSession; }
                status.Text(data.GetNamedString(L"status", active ? L"读取当前对局…" : L"暂无进行中的对局，等待进入加载界面。"));
                UpdateHint();
                auto teamNames = data.GetNamedArray(L"teams", JsonArray());
                for (uint32_t i = 0; i < 2 && i < teamNames.Size(); ++i) teams[i].Text(teamNames.GetStringAt(i));
                auto rows = data.GetNamedArray(L"players", JsonArray());
                populatedCards = 0;
                bool marked=false;
                for (uint32_t i = 0; i < 10; ++i)
                {
                    auto row = i < rows.Size() ? rows.GetObjectAt(i) : JsonObject();
                    auto& card = cards[i]; card.champion.Text(row.GetNamedString(L"champion", L"等待英雄"));
                    int hero=static_cast<int>(row.GetNamedNumber(L"championId",0));
                    if(selectedCard==static_cast<int>(i) && card.hero!=hero)CloseDetails(false);
                    card.hero=hero;
                    auto playerName = row.GetNamedString(L"name", L"空缺位置");
                    card.player.Text(playerName.empty() ? L"玩家名称尚未提供" : playerName); card.tier.Text(row.GetNamedString(L"tier", L"—"));
                    int partySize=static_cast<int>(row.GetNamedNumber(L"partySize",0));
                    card.partyColor=partySize>=2 && partySize<=5?static_cast<uint32_t>(row.GetNamedNumber(L"partyColor",0))&0xffffff:0;
                    card.partyLabel=card.partyColor?row.GetNamedString(L"partyLabel",L""):L"";
                    card.partyDetails=card.partyColor?row.GetNamedString(L"partyDetails",L""):L"";
                    card.party.Text(card.partyColor?row.GetNamedString(L"party",L""):L"");card.party.Foreground(MakeBrush(card.partyColor));
                    card.partyBadge.Visibility(card.partyColor?Visibility::Visible:Visibility::Collapsed);marked=marked || card.partyColor!=0;
                    card.player.Foreground(MakeBrush(card.partyColor?card.partyColor:0x91acbb));
                    card.button.BorderBrush(MakeBrush(card.partyColor?card.partyColor:0x315662));card.button.BorderThickness({card.partyColor?3.0:0.0,0,0,0});
                    ToolTip partyTip;auto partyText=MakeText(card.partyDetails,12);partyText.TextWrapping(TextWrapping::Wrap);partyText.MaxWidth(320);partyTip.Content(partyText);ToolTipService::SetToolTip(card.partyBadge,partyTip);
                    ToolTipService::SetToolTip(card.player,box_value(playerName+(card.partyDetails.empty()?L"":L"\n"+card.partyDetails)));
                    card.tier.Foreground(MakeBrush(static_cast<uint32_t>(row.GetNamedNumber(L"color", 0x91acbb))));
                    card.stats.Text(row.GetNamedString(L"stats", L"胜率 —    选用率 —"));
                    card.mastery.Text(row.GetNamedString(L"mastery", L""));
                    card.masteryDetails=row.GetNamedString(L"masteryDetails", L"");
                    ToolTipService::SetToolTip(card.mastery,box_value(card.masteryDetails));
                    card.deathStreak.Text(row.GetNamedString(L"deathStreak",L""));card.deathStreakDetails=row.GetNamedString(L"deathStreakDetails",L"");
                    ToolTipService::SetToolTip(card.deathStreak,box_value(card.deathStreakDetails));
                    card.details = row.GetNamedString(L"details", L"等待当前对局数据。");
                    card.button.IsEnabled(row.HasKey(L"name"));
                    if (row.HasKey(L"name")) ++populatedCards;
                    // Image decoding must never block the other nine players or the connection heartbeat.
                    auto image = row.GetNamedString(L"icon", L""); if (card.imageData != image) SetImage(i, image);
                }
                if (selectedCard >= 0) RefreshDetailHeader();
                else if (populatedCards > 0) details.Text(L"点击英雄卡片查看技能、出装与模式推荐；详情胜率属于英雄统计。"+hstring(marked?L"\n同色与人数标记：根据近期共同战绩推测的疑似组队。":L""));
                if(fixturePreview && !fixtureOpened && populatedCards>0){fixtureOpened=true;if(detailTab!=L"roster")OpenDetails(0);}
                auto nextCommand = data.GetNamedString(L"command", L"");
                if (widget && command != nextCommand)
                {
                    bool firstCommand = command.empty(); command = nextCommand;
                    if (enabled && active && data.GetNamedBoolean(L"show", false)) SetVisible(true);
                    else if (!firstCommand && (previouslyActive || !enabled)) SetVisible(false);
                }
            }
        }
        catch (hresult_error const& exception)
        {
            readError = to_hstring(uint32_t(exception.code())) + L" " + exception.message();
            if (Now() - lastGood > 15000 && !stopped)
            {
                active = false; revision = L""; Clear();
                populatedCards = 0;
                status.Text(connectionMessage);
                details.Text(L"连接恢复后会自动更新，无需重新打开组件。");
            }
        }
        if (!stopped && Now() - lastHeartbeat > 5000)
        {
            lastHeartbeat = Now();
            try
            {
                JsonObject state; state.Insert(L"protocol", JsonValue::CreateNumberValue(1));
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
                auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(widget ? L"widget-status.json" : L"preview-status.json", CreationCollisionOption::ReplaceExisting);
                co_await FileIO::WriteTextAsync(file, state.Stringify());
            }
            catch (...) {}
        }
        busy = false;
        UpdateChatButtons();
        if (capturePending && root.ActualWidth() > 0 && root.ActualHeight() > 0 && populatedCards > 0
            && (!fixturePreview || detailTab==L"roster" || (!detailRevision.empty() && pendingDetailImages==0)))
        {
            capturePending = false;
            CapturePreview();
        }
    }
};

struct WidgetApp : ApplicationT<WidgetApp>
{
    std::vector<std::shared_ptr<WidgetView>> views;
    WidgetApp()
    {
        UnhandledException([](auto&&, UnhandledExceptionEventArgs const& args) { LogError(args.Message()); });
    }
    void OnActivated(IActivatedEventArgs const& args)
    {
        Trace(L"activation kind=" + to_hstring(int(args.Kind())));
        auto activation = args.try_as<XboxGameBarWidgetActivatedEventArgs>();
        if (!activation) { Trace(L"activation-not-widget"); return; }
        if (!activation.IsLaunchActivation()) { Trace(L"widget-repeat-activation"); return; }
        Trace(L"widget-initial-activation");
        Frame frame; auto window = Window::Current();
        frame.HorizontalContentAlignment(HorizontalAlignment::Stretch);
        frame.VerticalContentAlignment(VerticalAlignment::Stretch);
        window.Content(frame);
        auto widget = XboxGameBarWidget(activation, window.CoreWindow(), frame);
        auto view = std::make_shared<WidgetView>();
        Page page; page.Content(view->root); frame.Content(page); view->Start(widget);
        std::erase_if(views, [](auto const& item) { return item->stopped; }); views.push_back(view);
        window.Closed([weak = std::weak_ptr<WidgetView>(view)](auto&&, auto&&) { if (auto value = weak.lock()) value->Stop(); });
        window.Activate();
        Trace(L"widget-window-activated");
    }
    void OnLaunched(LaunchActivatedEventArgs const& args)
    {
        // Use the identical renderer for a real desktop preview, without registering game shortcuts.
        Trace(L"desktop-preview-launch");
        auto window = Window::Current();
        if (!window.Content())
        {
            auto view = std::make_shared<WidgetView>(); Page page; page.Content(view->root);
            window.Content(page); view->capturePending = args.Arguments() == L"--capture-preview"; view->ConfigureDetailPreview(args.Arguments()); view->Start(nullptr);
            std::erase_if(views, [](auto const& item) { return item->stopped; }); views.push_back(view);
            window.Closed([weak = std::weak_ptr<WidgetView>(view)](auto&&, auto&&) { if (auto value = weak.lock()) value->Stop(); });
        }
        else if (args.Arguments() == L"--capture-preview")
        {
            for (auto const& view : views) if (!view->widget && !view->stopped) view->CapturePreview();
        }
        else
        {
            for(auto const& view:views)if(!view->widget && !view->stopped){view->ConfigureDetailPreview(args.Arguments());view->Read();}
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
        Application::Start([](auto&&) { make<WidgetApp>(); });
    }
    catch (hresult_error const& e) { LogError(to_hstring(uint32_t(e.code())) + L" " + e.message()); return 1; }
    return 0;
}
