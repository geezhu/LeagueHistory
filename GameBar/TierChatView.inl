// Included inside WidgetView. Statistics and confirmed voice text share the send gate.
StackPanel chatFooter;
// One send menu: pick the content, then the target team or the selected hero.
Button sendMenu, cancelChat;
MenuFlyoutSubItem tierMenu, masteryMenu, deathStreakMenu, recordMenu, partyMenu, waveChatMenu;
// Premades describe a side, so the menu offers the two teams only.
std::array<MenuFlyoutItem, 2> partyTargets;
// Wave summaries go to team chat; the targets name the content instead of players.
std::array<MenuFlyoutItem, 3> waveChatTargets;
static constexpr std::array<wchar_t const *, 3> WaveChatKeys{L"spawn", L"super", L"deaths"};
static constexpr wchar_t const *ChatIdleText =
    L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。";
std::array<MenuFlyoutItem, 3> tierTargets, masteryTargets, deathStreakTargets, recordTargets;
TextBlock chatStatus = MakeText(ChatIdleText, 12, 0x91acbb);
bool supportsChat = false, supportsMasteryChat = false, supportsDeathStreakChat = false, supportsRecordChat = false,
     supportsPartyChat = false, supportsControlChat = false, supportsWaveChat = false, chatTeams = false,
     chatPending = false, chatWriting = false, chatReading = false, chatDismissed = false, chatCancelled = false,
     chatCleanup = false;
hstring chatId, chatSession, chatTarget, chatKind, chatText, pendingChatWrite;
int chatSlot = -1, chatHero = 0, chatSpell = -1;
int64_t chatCreated = 0;

void InitializeChat()
{
    StackPanel actions;
    actions.Orientation(Orientation::Horizontal);
    sendMenu = DetailButton(L"发送 ▾");
    cancelChat = DetailButton(L"取消发送");
    for (auto b : {sendMenu, cancelChat})
    {
        b.Margin({0, 0, 6, 0});
        actions.Children().Append(b);
    }
    InitializeVoice();
    actions.Children().Append(voiceButton);
    waveButton.Margin({6, 0, 0, 0});
    actions.Children().Append(waveButton);
    controlButton.Margin({6, 0, 0, 0});
    actions.Children().Append(controlButton);
    compositionButton.Margin({6, 0, 0, 0});
    actions.Children().Append(compositionButton);
    augmentButton.Margin({6, 0, 0, 0});
    actions.Children().Append(augmentButton);
    MenuFlyout flyout;
    auto submenu =
        [this, &flyout](MenuFlyoutSubItem &sub, hstring title, std::array<MenuFlyoutItem, 3> &items, hstring kind)
    {
        std::array<hstring, 3> names{L"我方全部英雄", L"敌方全部英雄", L"当前选中的英雄"};
        std::array<hstring, 3> targets{L"own", L"enemy", L"single"};
        sub.Text(title);
        for (int i = 0; i < 3; ++i)
        {
            items[i].Text(names[i]);
            items[i].Click([this, kind, target = targets[i]](auto &&, auto &&) { QueueChat(target, kind); });
            sub.Items().Append(items[i]);
        }
        flyout.Items().Append(sub);
    };
    submenu(tierMenu, L"梯队与胜率", tierTargets, L"tier");
    submenu(masteryMenu, L"英雄成就", masteryTargets, L"mastery");
    submenu(deathStreakMenu, L"连死", deathStreakTargets, L"deathStreak");
    submenu(recordMenu, L"战绩与特征", recordTargets, L"record");
    partyMenu.Text(L"疑似组排");
    std::array<hstring, 2> partyNames{L"我方", L"敌方"};
    std::array<hstring, 2> partyKeys{L"own", L"enemy"};
    for (size_t i = 0; i < partyTargets.size(); ++i)
    {
        partyTargets[i].Text(partyNames[i]);
        partyTargets[i].Click([this, target = partyKeys[i]](auto &&, auto &&) { QueueChat(target, L"party"); });
        partyMenu.Items().Append(partyTargets[i]);
    }
    flyout.Items().Append(partyMenu);
    std::array<hstring, 3> waveNames{L"出波与炮车", L"超级兵", L"现在阵亡需等待"};
    waveChatMenu.Text(L"兵线（发到队伍）");
    for (int i = 0; i < 3; ++i)
    {
        waveChatTargets[i].Text(waveNames[i]);
        waveChatTargets[i].Click([this, target = hstring(WaveChatKeys[i])](auto &&, auto &&)
                                 { QueueChat(target, L"waves"); });
        waveChatMenu.Items().Append(waveChatTargets[i]);
    }
    flyout.Items().Append(waveChatMenu);
    sendMenu.Flyout(flyout);
    cancelChat.Click(
        [this](auto &&, auto &&)
        {
            if (chatPending)
            {
                chatCancelled = true;
                chatStatus.Text(L"正在取消发送…");
                SaveChatRequest();
                UpdateChatButtons();
            }
        });
    chatFooter.Margin({0, 8, 0, 0});
    chatFooter.Children().Append(actions);
    chatFooter.Children().Append(voicePanel);
    chatStatus.TextWrapping(TextWrapping::Wrap);
    chatStatus.Margin({0, 5, 0, 0});
    // The idle hint lives in the send button tooltip; the line appears only while a send is in progress.
    chatStatus.Visibility(Visibility::Collapsed);
    chatStatus.RegisterPropertyChangedCallback(
        TextBlock::TextProperty(),
        [this](auto &&, auto &&)
        { chatStatus.Visibility(chatStatus.Text() == ChatIdleText ? Visibility::Collapsed : Visibility::Visible); });
    chatFooter.Children().Append(chatStatus);
    Grid::SetRow(chatFooter, 4);
    root.Children().Append(chatFooter);
    UpdateChatButtons();
    ToolTipService::SetToolTip(
        sendMenu,
        box_value(
            L"发送梯队与胜率、英雄成就、连死或战绩与特征到游戏聊天，可选我方、敌方或当前选中英雄；兵线信息发到队伍聊天。\n"
            L"需以管理员身份运行 League；点击后按 Win＋G 返回游戏。连死：死亡加一，击杀清零，助攻不清零。\n"
            L"战绩与特征：近期胜率和行为标签，与 F9 / F11 相同，按「发送战绩时隐藏自己」和「使用英雄名称」设置。\n"
            L"疑似组排：按近期同队战绩推测的 n 排 / 单排 / 未知，不能确认当前实际组队。"));
}
void UpdateChatButtons()
{
    bool available = widget && !fixturePreview && supportsChat && enabled && active && !chatPending && !chatCleanup &&
                     !voiceRunning && !voiceCleanup && Now() - lastGood <= 15000;
    bool single = selectedCard >= 0 && cards[selectedCard].hero > 0;
    bool targets = chatTeams || single;
    bool waveReady = available && supportsWaveChat && HasWaves();
    sendMenu.IsEnabled(available && (targets || waveReady));
    tierMenu.IsEnabled(available && targets);
    masteryMenu.IsEnabled(available && supportsMasteryChat && targets);
    masteryMenu.Visibility(supportsMasteryChat ? Visibility::Visible : Visibility::Collapsed);
    deathStreakMenu.IsEnabled(available && supportsDeathStreakChat && targets);
    deathStreakMenu.Visibility(supportsDeathStreakChat ? Visibility::Visible : Visibility::Collapsed);
    recordMenu.IsEnabled(available && supportsRecordChat && targets);
    recordMenu.Visibility(supportsRecordChat ? Visibility::Visible : Visibility::Collapsed);
    partyMenu.IsEnabled(available && supportsPartyChat && chatTeams);
    partyMenu.Visibility(supportsPartyChat ? Visibility::Visible : Visibility::Collapsed);
    for (auto &item : partyTargets)
        item.IsEnabled(available && supportsPartyChat && chatTeams);
    controlChatButton.Visibility(supportsControlChat ? Visibility::Visible : Visibility::Collapsed);
    controlChatButton.IsEnabled(available && supportsControlChat && chatTeams);
    for (auto &item : controlChatTargets)
        item.IsEnabled(available && supportsControlChat && chatTeams);
    bool compositionChat = supportsCompositionChat && composition != nullptr;
    compositionChatButton.Visibility(compositionChat ? Visibility::Visible : Visibility::Collapsed);
    compositionChatButton.IsEnabled(available && compositionChat && chatTeams);
    for (auto &item : compositionChatTargets)
        item.IsEnabled(available && compositionChat && chatTeams);
    for (int i = 0; i < 3; ++i)
    {
        bool scope = i == 2 ? single : chatTeams;
        tierTargets[i].IsEnabled(available && scope);
        masteryTargets[i].IsEnabled(available && supportsMasteryChat && scope);
        deathStreakTargets[i].IsEnabled(available && supportsDeathStreakChat && scope);
        recordTargets[i].IsEnabled(available && supportsRecordChat && scope);
    }
    waveChatMenu.Visibility(supportsWaveChat ? Visibility::Visible : Visibility::Collapsed);
    waveChatMenu.IsEnabled(waveReady);
    for (int i = 0; i < 3; ++i)
        waveChatTargets[i].IsEnabled(waveReady && WaveChatAvailable(WaveChatKeys[i]));
    cancelChat.Visibility(chatPending ? Visibility::Visible : Visibility::Collapsed);
    cancelChat.IsEnabled(!chatCancelled);
    UpdateVoiceButtons();
}
void ResetChat()
{
    if (chatKind == L"voice" && !chatId.empty())
        ForgetVoiceRequest();
    chatPending = false;
    chatId = L"";
    chatText = L"";
    pendingChatWrite = L"";
    chatStatus.Text(ChatIdleText);
    UpdateChatButtons();
}
bool CanQueueChat(hstring const &target, hstring const &kind) const
{
    if (stopped || !widget || fixturePreview || !supportsChat || chatPending || chatCleanup || voiceRunning ||
        voiceCleanup || !enabled || !active || Now() - lastGood > 15000)
        return false;
    if (kind == L"waves")
        return supportsWaveChat && HasWaves() && WaveChatAvailable(target);
    if (kind == L"summoner")
        return supportsSummonerChat && target == L"single" && summonerChatCard >= 0 && summonerChatCard < 5 &&
               cards[summonerChatCard].hero > 0;
    if ((kind != L"tier" && kind != L"mastery" && kind != L"deathStreak" && kind != L"record" && kind != L"party" &&
         kind != L"controls" && kind != L"composition" && kind != L"voice") ||
        (kind == L"mastery" && !supportsMasteryChat) || (kind == L"deathStreak" && !supportsDeathStreakChat) ||
        (kind == L"record" && !supportsRecordChat) ||
        (kind == L"party" && (!supportsPartyChat || target == L"single")) ||
        (kind == L"controls" && (!supportsControlChat || target == L"single")) ||
        (kind == L"composition" && (!supportsCompositionChat || !composition || target == L"single")))
        return false;
    if (kind != L"voice" && target != L"own" && target != L"enemy" && target != L"single")
    {
        return false;
    }
    if (kind == L"voice" && (!supportsVoiceChat || target != L"text" || chatText.empty()))
        return false;
    if (target == L"single" && (selectedCard < 0 || cards[selectedCard].hero <= 0))
        return false;
    if (kind != L"voice" && target != L"single" && !chatTeams)
        return false;
    return true;
}
void QueueChat(hstring const &target, hstring const &kind)
{
    if (!CanQueueChat(target, kind))
    {
        return;
    }
    if (kind != L"voice")
        chatText = L"";
    std::wstring id;
    for (wchar_t c : NewDetailId())
        if (c != L'{' && c != L'}' && c != L'-')
            id.push_back(c >= L'A' && c <= L'F' ? c + 32 : c);
    chatId = hstring(id);
    chatSession = session;
    chatTarget = target;
    chatKind = kind;
    chatCreated = Now();
    chatCancelled = false;
    chatPending = true;
    chatSlot = kind == L"summoner" ? summonerChatCard : target == L"single" ? selectedCard : -1;
    chatHero = chatSlot >= 0 ? cards[chatSlot].hero : 0;
    chatSpell = kind == L"summoner" ? summonerChatSpell : -1;
    auto name = kind == L"waves"     ? L"兵线"
                : kind == L"voice"   ? L"已确认的文字"
                : target == L"own"   ? L"我方英雄"
                : target == L"enemy" ? L"敌方英雄"
                                     : cards[chatSlot].champion.Text();
    hstring metric = kind == L"summoner" ? L" " + cards[chatSlot].summoners[chatSpell].name
                                         : hstring(kind == L"mastery"       ? L"熟练度"
                                                   : kind == L"deathStreak" ? L"连死次数"
                                                   : kind == L"record"      ? L"战绩"
                                                   : kind == L"party"       ? L"疑似组排"
                                                   : kind == L"controls"    ? L"控制技能"
                                                   : kind == L"composition" ? L"阵容能力"
                                                   : target == L"spawn"     ? L"出波与炮车"
                                                   : target == L"super"     ? L"超级兵"
                                                   : target == L"deaths"    ? L"阵亡代价"
                                                                            : L"梯队");
    chatStatus.Text(kind == L"voice" ? L"文字已确认。请在 20 秒内按 Win＋G 返回游戏，保持聊天输入框关闭；可取消发送。"
                                     : L"待发送：" + hstring(name) + metric +
                                           L"。请按 Win＋G 返回游戏，保持聊天输入框关闭（20 秒内）。");
    SaveChatRequest();
    UpdateChatButtons();
}
void SaveChatRequest()
{
    if (!widget || fixturePreview || !chatPending || stopped)
        return;
    chatDismissed = widget.GameBarDisplayMode() == XboxGameBarDisplayMode::PinnedOnly;
    auto request = CreateChatRequest();
    pendingChatWrite = request.Stringify();
    WriteChatRequest();
}
JsonObject CreateChatRequest() const
{
    JsonObject request;
    request.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    request.Insert(L"id", JsonValue::CreateStringValue(chatId));
    request.Insert(L"session", JsonValue::CreateStringValue(chatSession));
    request.Insert(L"target", JsonValue::CreateStringValue(chatTarget));
    request.Insert(L"kind", JsonValue::CreateStringValue(chatKind));
    if (chatKind == L"voice")
    {
        request.Insert(L"text", JsonValue::CreateStringValue(chatText));
        request.Insert(L"confirmed", JsonValue::CreateBooleanValue(true));
    }
    if (chatKind == L"summoner")
        request.Insert(L"spell", JsonValue::CreateNumberValue(chatSpell));
    request.Insert(L"slot", JsonValue::CreateNumberValue(chatSlot));
    request.Insert(L"championId", JsonValue::CreateNumberValue(chatHero));
    request.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(chatCreated)));
    request.Insert(L"dismissed", JsonValue::CreateBooleanValue(chatDismissed));
    request.Insert(L"cancelled", JsonValue::CreateBooleanValue(chatCancelled));
    return request;
}
fire_and_forget WriteChatRequest()
{
    if (chatWriting || stopped)
        co_return;
    auto lifetime = shared_from_this();
    chatWriting = true;
    try
    {
        while (!pendingChatWrite.empty() && !stopped)
        {
            auto value = pendingChatWrite;
            pendingChatWrite = L"";
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                L"chat-request.tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, value);
            co_await file.RenameAsync(L"chat-request.json", NameCollisionOption::ReplaceExisting);
        }
    }
    catch (...)
    {
        chatPending = false;
        pendingChatWrite = L"";
        chatStatus.Text(L"发送请求未能写入，请重试。");
        if (chatKind == L"voice")
            ForgetVoiceRequest();
    }
    chatWriting = false;
    UpdateChatButtons();
}
fire_and_forget ReadChatStatus()
{
    if (stopped || fixturePreview || !widget || !chatPending || chatReading)
        co_return;
    auto lifetime = shared_from_this();
    chatReading = true;
    auto id = chatId;
    try
    {
        if ((widget.GameBarDisplayMode() == XboxGameBarDisplayMode::PinnedOnly) != chatDismissed)
            SaveChatRequest();
        auto file = co_await ApplicationData::Current().LocalFolder().GetFileAsync(L"chat-status.json");
        auto properties = co_await file.GetBasicPropertiesAsync();
        if (properties.Size() > 8192)
            throw hresult_invalid_argument();
        auto text = co_await FileIO::ReadTextAsync(file);
        auto data = JsonObject::Parse(text);
        ApplyChatStatus(data, id);
    }
    catch (...)
    {
        // An atomic bridge update may temporarily remove the reply; keep polling until completion or timeout.
    }
    if (!stopped && chatPending && Now() - chatCreated > 32000)
    {
        chatPending = false;
        chatStatus.Text(L"发送请求已超时，请确认主程序正在运行后重试。");
        if (chatKind == L"voice")
            ForgetVoiceRequest();
    }
    chatReading = false;
    UpdateChatButtons();
}
void ApplyChatStatus(JsonObject const &data, hstring const &id)
{
    if (!stopped && id == chatId && data.GetNamedNumber(L"protocol", 0) == 1 &&
        data.GetNamedString(L"requestId", L"") == chatId && data.GetNamedString(L"session", L"") == chatSession)
    {
        chatStatus.Text(data.GetNamedString(L"message", L"正在处理发送…"));
        if (data.GetNamedBoolean(L"complete", false))
        {
            chatPending = false;
            if (chatKind == L"voice")
            {
                if (data.GetNamedString(L"state", L"") == L"input-complete")
                    voiceEditor.Text(L"");
                SetVoiceHint(L"");
                ForgetVoiceRequest();
            }
        }
    }
}
fire_and_forget ForgetVoiceRequest()
{
    if (chatCleanup)
        co_return;
    auto lifetime = shared_from_this();
    apartment_context ui;
    auto id = chatId;
    chatCleanup = true;
    try
    {
        while (chatWriting)
        {
            co_await resume_after(std::chrono::milliseconds(100));
            co_await ui;
        }
        auto file = co_await ApplicationData::Current().LocalFolder().GetFileAsync(L"chat-request.json");
        auto properties = co_await file.GetBasicPropertiesAsync();
        if (properties.Size() <= 4096)
        {
            auto data = JsonObject::Parse(co_await FileIO::ReadTextAsync(file));
            if (data.GetNamedString(L"id", L"") == id && data.GetNamedString(L"kind", L"") == L"voice")
                co_await file.DeleteAsync();
        }
    }
    catch (...)
    {
        // Cleanup is idempotent: the main process may have consumed and removed the request already.
    }
    chatText = L"";
    chatCleanup = false;
    if (!stopped)
        UpdateChatButtons();
}
