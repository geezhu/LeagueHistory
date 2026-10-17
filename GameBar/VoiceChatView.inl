// Included inside WidgetView. Recognition runs in the main process with the local Chinese engine.
Button voiceButton, voiceRecord, voiceSend, voiceClose;
hstring voiceRecordLabel;
Border voicePanel;
TextBox voiceEditor;
TextBlock voiceHint = MakeText(L"", 12, 0x91acbb);
TextBlock voiceCount = MakeText(L"0 / 180", 11, 0x91acbb);
bool supportsVoiceChat = false, supportsVoiceRecording = false, voiceRunning = false, voiceReady = false,
     voiceStopRequested = false;
bool voiceReading = false, voiceWriting = false, voiceCleanup = false, voiceDecoding = false;
uint64_t voiceGeneration = 0;
hstring voiceId, voiceSession, voiceAction, pendingVoiceWrite;
int64_t voiceCreated = 0, voiceAck = 0;

void SetVoiceHint(hstring const &text)
{
    voiceHint.Text(text);
    voiceHint.Visibility(text.empty() ? Visibility::Collapsed : Visibility::Visible);
}

void InitializeVoice()
{
    voiceButton = DetailButton(L"");
    voiceButton.Content(SymbolIcon(Symbol::Microphone));
    Windows::UI::Xaml::Automation::AutomationProperties::SetName(voiceButton, L"语音输入");
    voiceButton.Margin({0, 0, 6, 0});
    voiceButton.Click([this](auto &&, auto &&) { OpenVoice(); });
    ToolTipService::SetToolTip(
        voiceButton, box_value(L"语音输入：说话后编辑文字，确认发送，再按 Win＋G 返回游戏。也可以直接输入文字。"));
    voicePanel.Background(MakeBrush(0x102f3c));
    voicePanel.BorderBrush(MakeBrush(0x315662));
    voicePanel.BorderThickness({1});
    voicePanel.CornerRadius({4});
    voicePanel.Padding({6, 6, 6, 4});
    voicePanel.Margin({0, 0, 0, 0});
    voicePanel.Visibility(Visibility::Collapsed);
    voicePanel.Child(CreateVoiceRow());
}
// One row like a chat box: microphone toggle, the single chat line, send and close.
// Clearing is plain editing; Esc closes; the status and length share one small line below.
Grid CreateVoiceRow()
{
    Grid row;
    for (auto width : {GridLength{1, GridUnitType::Auto},
                       GridLength{1, GridUnitType::Star},
                       GridLength{1, GridUnitType::Auto},
                       GridLength{1, GridUnitType::Auto}})
    {
        ColumnDefinition column;
        column.Width(width);
        row.ColumnDefinitions().Append(column);
    }
    row.RowDefinitions().Append(RowDefinition());
    row.RowDefinitions().Append(RowDefinition());
    voiceRecord = DetailButton(L"");
    voiceRecord.Padding({8, 4, 8, 4});
    voiceRecord.Click(
        [this](auto &&, auto &&)
        {
            if (voiceRunning)
                RequestVoiceStop();
            else
                RecognizeVoice();
        });
    row.Children().Append(voiceRecord);
    ConfigureVoiceEditor();
    Grid::SetColumn(voiceEditor, 1);
    row.Children().Append(voiceEditor);
    voiceSend = DetailButton(L"发送");
    voiceSend.Padding({14, 4, 14, 4});
    voiceSend.Background(MakeBrush(0x007f8f));
    voiceSend.Click([this](auto &&, auto &&) { ConfirmVoice(); });
    Grid::SetColumn(voiceSend, 2);
    row.Children().Append(voiceSend);
    voiceClose = DetailButton(L"");
    voiceClose.Content(SymbolIcon(Symbol::Cancel));
    voiceClose.Padding({8, 4, 8, 4});
    voiceClose.Margin({6, 0, 0, 0});
    voiceClose.Click([this](auto &&, auto &&) { CloseVoice(); });
    ToolTipService::SetToolTip(voiceClose, box_value(L"关闭语音输入（Esc）"));
    Windows::UI::Xaml::Automation::AutomationProperties::SetName(voiceClose, L"关闭语音输入");
    Grid::SetColumn(voiceClose, 3);
    row.Children().Append(voiceClose);
    voiceHint.TextWrapping(TextWrapping::NoWrap);
    voiceHint.Margin({2, 3, 0, 0});
    voiceHint.Visibility(Visibility::Collapsed);
    Grid::SetRow(voiceHint, 1);
    Grid::SetColumnSpan(voiceHint, 2);
    row.Children().Append(voiceHint);
    voiceCount.FontSize(11);
    voiceCount.HorizontalAlignment(HorizontalAlignment::Right);
    voiceCount.Margin({0, 3, 2, 0});
    Grid::SetRow(voiceCount, 1);
    Grid::SetColumn(voiceCount, 1);
    Grid::SetColumnSpan(voiceCount, 3);
    row.Children().Append(voiceCount);
    return row;
}
void ConfigureVoiceEditor()
{
    voiceEditor.MaxLength(180);
    voiceEditor.AcceptsReturn(false);
    voiceEditor.TextWrapping(TextWrapping::NoWrap);
    voiceEditor.PlaceholderText(L"说话或直接输入，Enter 发送");
    UseChineseInput(voiceEditor);
    voiceEditor.FontSize(14);
    voiceEditor.Margin({6, 0, 6, 0});
    voiceEditor.VerticalAlignment(VerticalAlignment::Center);
    voiceEditor.Background(MakeBrush(0x071e29));
    voiceEditor.Foreground(MakeBrush(0xe2eef3));
    voiceEditor.BorderBrush(MakeBrush(0x315662));
    voiceEditor.TextChanged([this](auto &&, auto &&) { UpdateVoiceButtons(); });
    voiceEditor.KeyDown(
        [this](auto &&, Windows::UI::Xaml::Input::KeyRoutedEventArgs const &e)
        {
            // Enter is the explicit confirmation; Esc closes like the close button.
            if (e.Key() == Windows::System::VirtualKey::Enter)
            {
                e.Handled(true);
                if (voiceSend.IsEnabled())
                    ConfirmVoice();
            }
            else if (e.Key() == Windows::System::VirtualKey::Escape && voiceClose.IsEnabled())
            {
                e.Handled(true);
                CloseVoice();
            }
        });
}
bool VoiceAvailable() const
{
    return widget && !fixturePreview && supportsChat && supportsVoiceChat && enabled && active &&
           Now() - lastGood <= 15000 && !chatPending && !chatCleanup && !voiceCleanup &&
           widget.GameBarDisplayMode() == XboxGameBarDisplayMode::Foreground;
}
void UpdateVoiceButtons()
{
    if (!voiceButton)
        return;
    bool available = VoiceAvailable();
    hstring text;
    voiceButton.IsEnabled(available && !voiceRunning);
    voiceEditor.IsReadOnly(voiceRunning || chatPending || chatCleanup);
    voiceRecord.IsEnabled(available && supportsVoiceRecording &&
                          (!voiceRunning || (voiceReady && !voiceStopRequested)));
    // Microphone to start, a stop square while listening, an ellipsis while preparing or decoding.
    hstring recordLabel = VoiceRecordLabel();
    bool listening = voiceRunning && voiceReady && !voiceStopRequested && !voiceDecoding;
    if (voiceRecordLabel != recordLabel)
    {
        voiceRecordLabel = recordLabel;
        voiceRecord.Content(SymbolIcon(listening ? Symbol::Stop : voiceRunning ? Symbol::More : Symbol::Microphone));
        ToolTipService::SetToolTip(voiceRecord, box_value(recordLabel));
        Windows::UI::Xaml::Automation::AutomationProperties::SetName(voiceRecord, recordLabel);
    }
    voiceRecord.Background(MakeBrush(listening ? 0x7a2c2c : 0x143542));
    voiceSend.IsEnabled(available && !voiceRunning && ConfirmedVoiceText(voiceEditor.Text(), text));
    voiceClose.IsEnabled(!chatPending && !chatCleanup);
    voiceCount.Text(to_hstring(voiceEditor.Text().size()) + L"/180");
}
hstring VoiceRecordLabel() const
{
    return voiceRunning                 ? (voiceDecoding        ? L"识别中…"
                                           : voiceStopRequested ? L"停止中…"
                                           : voiceReady         ? L"停止录音"
                                                                : L"准备中…")
           : voiceEditor.Text().empty() ? L"开始录音"
                                        : L"重新录音";
}
void OpenVoice()
{
    if (!VoiceAvailable())
        return;
    if (!supportsVoiceRecording)
        SetVoiceHint(L"请启动新版 League 主程序以使用本机语音识别；仍可直接输入文字。");
    voicePanel.Visibility(Visibility::Visible);
    ShowSendActions(false);
    UpdateVoiceButtons();
    voiceEditor.Focus(FocusState::Programmatic);
    // The user pressed the microphone, so listening starts now; sending still needs Enter or the send button.
    if (supportsVoiceRecording)
        RecognizeVoice();
}
// The voice row stands in for the send menu; cancel stays available for a pending send.
void ShowSendActions(bool visible)
{
    sendMenu.Visibility(visible ? Visibility::Visible : Visibility::Collapsed);
    voiceButton.Visibility(visible ? Visibility::Visible : Visibility::Collapsed);
}
void CloseVoice()
{
    if (!voiceId.empty())
    {
        voiceAction = L"cancel";
        SaveVoiceRequest();
        ForgetRecording();
    }
    ++voiceGeneration;
    voiceRunning = false;
    voiceReady = false;
    voiceDecoding = false;
    voiceStopRequested = true;
    voicePanel.Visibility(Visibility::Collapsed);
    ShowSendActions(true);
    voiceEditor.Text(L"");
    SetVoiceHint(L"");
    if (!stopped)
        UpdateChatButtons();
}
void RequestVoiceStop()
{
    if (!voiceRunning || voiceStopRequested)
        return;
    voiceStopRequested = true;
    voiceAction = L"stop";
    SaveVoiceRequest();
    if (!stopped)
        UpdateVoiceButtons();
}
bool VoiceCurrent(uint64_t generation) const
{
    return !stopped && generation == voiceGeneration && voicePanel.Visibility() == Visibility::Visible;
}
void RecognizeVoice()
{
    if (!VoiceAvailable() || !supportsVoiceRecording || voiceRunning || voicePanel.Visibility() != Visibility::Visible)
        return;
    ++voiceGeneration;
    voiceId = NewRequestId();
    voiceSession = session;
    voiceCreated = Now();
    voiceAck = 0;
    voiceAction = L"start";
    voiceRunning = true;
    voiceReady = false;
    voiceDecoding = false;
    voiceStopRequested = false;
    voiceEditor.Text(L"");
    SetVoiceHint(L"");
    SaveVoiceRequest();
    UpdateChatButtons();
    Trace(L"voice-local-start");
}
void SaveVoiceRequest()
{
    if (!widget || fixturePreview || voiceId.empty())
        return;
    pendingVoiceWrite = VoiceRequestJson(voiceId, voiceSession, voiceCreated, voiceAction, voiceAck).Stringify();
    WriteVoiceRequest();
}
fire_and_forget WriteVoiceRequest()
{
    if (voiceWriting)
        co_return;
    auto lifetime = shared_from_this();
    voiceWriting = true;
    try
    {
        while (!pendingVoiceWrite.empty())
        {
            auto value = pendingVoiceWrite;
            pendingVoiceWrite = L"";
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                L"voice-request.tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, value);
            co_await file.RenameAsync(L"voice-request.json", NameCollisionOption::ReplaceExisting);
        }
    }
    catch (...)
    {
        voiceRunning = false;
        pendingVoiceWrite = L"";
        SetVoiceHint(L"录音请求未能同步，请重试。");
        Trace(L"voice-local-write-error");
    }
    voiceWriting = false;
    if (!stopped)
        UpdateChatButtons();
}
fire_and_forget ReadVoiceStatus()
{
    if (stopped || fixturePreview || !widget || !voiceRunning || voiceReading || voiceCleanup)
        co_return;
    if (Now() - lastGood > 15000)
    {
        CloseVoice();
        co_return;
    }
    auto lifetime = shared_from_this();
    auto id = voiceId;
    auto generation = voiceGeneration;
    voiceReading = true;
    SaveVoiceRequest();
    try
    {
        auto file = co_await ApplicationData::Current().LocalFolder().GetFileAsync(L"voice-status.json");
        auto properties = co_await file.GetBasicPropertiesAsync();
        if (properties.Size() > 8192)
            throw hresult_invalid_argument();
        auto data = JsonObject::Parse(co_await FileIO::ReadTextAsync(file));
        ApplyVoiceStatus(data, generation, id);
    }
    catch (...)
    {
        // The main process atomically replaces status files; a missing or partial reply is retried by the next poll.
    }
    if (VoiceCurrent(generation) && voiceRunning && Now() - voiceCreated > 55000)
    {
        voiceAction = L"cancel";
        SaveVoiceRequest();
        voiceRunning = false;
        voiceReady = false;
        ForgetRecording();
        SetVoiceHint(L"录音响应超时，请确认新版 League 正在运行，再重新录音。");
        Trace(L"voice-local-timeout");
    }
    voiceReading = false;
    if (!stopped)
        UpdateChatButtons();
}
bool ApplyVoiceStatus(JsonObject const &data, uint64_t generation, hstring const &id)
{
    if (!voiceRunning || !VoiceCurrent(generation) || id != voiceId ||
        !VoiceStatusAccepted(data, id, voiceSession, voiceAck))
        return false;
    voiceAck = static_cast<int64_t>(data.GetNamedNumber(L"sequence", 0));
    voiceEditor.Text(ClipVoice(std::wstring(data.GetNamedString(L"text", L"")), 180));
    auto state = data.GetNamedString(L"state", L"");
    SetVoiceHint(state == L"preparing" || state == L"decoding" || state == L"stopping"
                     ? L""
                     : data.GetNamedString(L"hint", L""));
    voiceReady = state == L"recording";
    voiceDecoding = data.GetNamedString(L"state", L"") == L"decoding";
    if (data.GetNamedBoolean(L"complete", false))
    {
        voiceRunning = false;
        voiceReady = false;
        voiceDecoding = false;
        if (!fixturePreview)
            ForgetRecording();
        Trace(L"voice-local-complete");
    }
    else
        SaveVoiceRequest();
    return true;
}
fire_and_forget ForgetRecording()
{
    if (voiceCleanup || voiceId.empty())
        co_return;
    auto lifetime = shared_from_this();
    apartment_context ui;
    auto id = voiceId;
    bool cancelled = voiceAction == L"cancel";
    voiceCleanup = true;
    try
    {
        while (voiceWriting)
        {
            co_await resume_after(std::chrono::milliseconds(50));
            co_await ui;
        }
        // Give the main 400ms poll an opportunity to observe the explicit cancellation.
        if (cancelled)
        {
            co_await resume_after(std::chrono::milliseconds(600));
            co_await ui;
        }
        for (auto name : {L"voice-request.json", L"voice-status.json"})
        {
            try
            {
                auto file = co_await ApplicationData::Current().LocalFolder().GetFileAsync(name);
                auto properties = co_await file.GetBasicPropertiesAsync();
                if (properties.Size() <= 8192)
                {
                    auto data = JsonObject::Parse(co_await FileIO::ReadTextAsync(file));
                    if (data.GetNamedString(L"id", L"") == id)
                        co_await file.DeleteAsync();
                }
            }
            catch (...)
            {
                // The main process may already have removed this completed recording's files.
            }
        }
    }
    catch (...)
    {
        Trace(L"voice-local-cleanup-error");
    }
    if (voiceId == id)
        voiceId = L"";
    voiceCleanup = false;
    if (!stopped)
        UpdateChatButtons();
}
void ConfirmVoice()
{
    hstring text;
    if (!VoiceAvailable() || voiceRunning)
        return;
    if (!ConfirmedVoiceText(voiceEditor.Text(), text))
    {
        SetVoiceHint(L"请输入一条不超过 180 字符的聊天文字；不支持换行或斜杠命令。");
        return;
    }
    chatText = text;
    QueueChat(L"text", L"voice");
}
fire_and_forget ConfigureVoicePreview()
{
    auto lifetime = shared_from_this();
    voicePanel.Visibility(Visibility::Visible);
    ShowSendActions(false);
    try
    {
        // Exercise the production result handler with a fixture. No request, audio or chat is produced.
        ++voiceGeneration;
        voiceId = L"preview-recording";
        voiceSession = L"preview-session";
        voiceRunning = true;
        voiceAck = 0;
        voiceEditor.Text(L"");
        JsonObject result;
        result.Insert(L"protocol", JsonValue::CreateNumberValue(1));
        result.Insert(L"id", JsonValue::CreateStringValue(L"wrong-id"));
        result.Insert(L"session", JsonValue::CreateStringValue(voiceSession));
        result.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(Now())));
        result.Insert(L"sequence", JsonValue::CreateNumberValue(1));
        result.Insert(L"state", JsonValue::CreateStringValue(L"recording"));
        result.Insert(L"text", JsonValue::CreateStringValue(L"错误会话文字"));
        bool ignored = !ApplyVoiceStatus(result, voiceGeneration, voiceId) && voiceEditor.Text().empty();
        result.Insert(L"id", JsonValue::CreateStringValue(voiceId));
        result.Insert(L"text", JsonValue::CreateStringValue(L"先别开团，等队友到齐后一起推。注意对面绕后。"));
        bool delivered =
            ApplyVoiceStatus(result, voiceGeneration, voiceId) && voiceReady && !voiceEditor.Text().empty();
        result.Insert(L"text", JsonValue::CreateStringValue(L"旧结果"));
        bool oldIgnored = !ApplyVoiceStatus(result, voiceGeneration, voiceId) && voiceEditor.Text() != L"旧结果";
        result.Insert(L"sequence", JsonValue::CreateNumberValue(2));
        result.Insert(L"state", JsonValue::CreateStringValue(L"decoding"));
        result.Insert(L"text", JsonValue::CreateStringValue(voiceEditor.Text()));
        bool decoding = ApplyVoiceStatus(result, voiceGeneration, voiceId) && voiceDecoding && !voiceReady;
        result.Insert(L"sequence", JsonValue::CreateNumberValue(3));
        result.Insert(L"complete", JsonValue::CreateBooleanValue(true));
        result.Insert(L"text", JsonValue::CreateStringValue(voiceEditor.Text()));
        bool completed = ApplyVoiceStatus(result, voiceGeneration, voiceId) && !voiceRunning && !voiceReady;
        voiceId = L"";
        SetVoiceHint(L"");
        UpdateVoiceButtons();
        JsonObject info;
        info.Insert(L"backend", JsonValue::CreateStringValue(L"main-local-Chinese"));
        info.Insert(L"microphoneOpened", JsonValue::CreateBooleanValue(false));
        info.Insert(L"resultDeliveryChecked",
                    JsonValue::CreateBooleanValue(ignored && delivered && oldIgnored && decoding && completed));
        info.Insert(L"engine", JsonValue::CreateStringValue(L"SenseVoice"));
        auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
            L"voice-capabilities-preview.json", CreationCollisionOption::ReplaceExisting);
        co_await FileIO::WriteTextAsync(file, info.Stringify());
    }
    catch (...)
    {
        Trace(L"voice-capabilities-preview-unavailable");
    }
}
