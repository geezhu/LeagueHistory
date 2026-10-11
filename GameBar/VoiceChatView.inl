// Included inside WidgetView. Recognition runs in the main process with the local Chinese engine.
Button voiceButton, voiceRecord, voiceSend, voiceClear, voiceClose;
Border voicePanel;
TextBox voiceEditor;
TextBlock voiceHint = MakeText(L"", 12, 0x91acbb);
TextBlock voiceCount = MakeText(L"0 / 180", 12, 0x91acbb);
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

static hstring ClipVoice(std::wstring text, size_t limit)
{
    if (text.size() > limit)
    {
        text.resize(limit);
        if (!text.empty() && text.back() >= 0xd800 && text.back() <= 0xdbff)
            text.pop_back();
    }
    return hstring(text);
}
static bool ConfirmedVoiceText(hstring const &value, hstring &result)
{
    std::wstring text(value);
    if (text.size() > 180)
        return false;
    for (size_t i = 0; i < text.size(); ++i)
    {
        auto c = text[i];
        if (c < 32 || (c >= 0x7f && c <= 0x9f) || c == 0x2028 || c == 0x2029)
            return false;
        if (c >= 0xd800 && c <= 0xdbff)
        {
            if (++i >= text.size() || text[i] < 0xdc00 || text[i] > 0xdfff)
                return false;
        }
        else if (c >= 0xdc00 && c <= 0xdfff)
            return false;
    }
    auto first = text.find_first_not_of(
        L" \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u202f\u205f\u3000");
    if (first == std::wstring::npos || text[first] == L'/')
        return false;
    auto last = text.find_last_not_of(
        L" \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u202f\u205f\u3000");
    result = hstring(text.substr(first, last - first + 1));
    return true;
}
void InitializeVoice()
{
    voiceButton = DetailButton(L"语音输入");
    voiceButton.Margin({0, 0, 6, 0});
    voiceButton.Click([this](auto &&, auto &&) { OpenVoice(); });
    ToolTipService::SetToolTip(voiceButton,
                               box_value(L"说话后编辑文字，确认发送，再按 Win＋G 返回游戏。也可以直接输入文字。"));
    voicePanel.Background(MakeBrush(0x102f3c));
    voicePanel.BorderBrush(MakeBrush(0x315662));
    voicePanel.BorderThickness({1});
    voicePanel.CornerRadius({4});
    voicePanel.Padding({10, 8, 10, 8});
    voicePanel.Margin({0, 6, 0, 0});
    voicePanel.Visibility(Visibility::Collapsed);
    StackPanel body;
    body.Children().Append(CreateVoiceHeader());
    ConfigureVoiceEditor();
    body.Children().Append(voiceEditor);
    body.Children().Append(CreateVoiceFooter());
    voiceHint.TextWrapping(TextWrapping::Wrap);
    voiceHint.Margin({0, 5, 0, 0});
    voiceHint.Visibility(Visibility::Collapsed);
    body.Children().Append(voiceHint);
    voicePanel.Child(body);
}
Grid CreateVoiceHeader()
{
    Grid header;
    header.ColumnDefinitions().Append(ColumnDefinition());
    ColumnDefinition buttons;
    buttons.Width({1, GridUnitType::Auto});
    header.ColumnDefinitions().Append(buttons);
    auto title = MakeText(L"语音输入", 14, 0x65d6d6);
    title.VerticalAlignment(VerticalAlignment::Center);
    header.Children().Append(title);
    StackPanel actions;
    actions.Orientation(Orientation::Horizontal);
    Grid::SetColumn(actions, 1);
    voiceRecord = DetailButton(L"开始录音");
    voiceRecord.Padding({10, 4, 10, 4});
    voiceRecord.Margin({0, 0, 6, 0});
    voiceRecord.Click(
        [this](auto &&, auto &&)
        {
            if (voiceRunning)
                RequestVoiceStop();
            else
                RecognizeVoice();
        });
    actions.Children().Append(voiceRecord);
    voiceClose = DetailButton(L"关闭");
    voiceClose.Padding({10, 4, 10, 4});
    voiceClose.Click([this](auto &&, auto &&) { CloseVoice(); });
    actions.Children().Append(voiceClose);
    header.Children().Append(actions);
    return header;
}
void ConfigureVoiceEditor()
{
    voiceEditor.MaxLength(180);
    voiceEditor.AcceptsReturn(false);
    voiceEditor.TextWrapping(TextWrapping::Wrap);
    voiceEditor.MinHeight(58);
    voiceEditor.MaxHeight(80);
    voiceEditor.PlaceholderText(L"录音后检查文字，或直接输入…");
    voiceEditor.FontSize(14);
    voiceEditor.Margin({0, 6, 0, 6});
    voiceEditor.Background(MakeBrush(0x071e29));
    voiceEditor.Foreground(MakeBrush(0xe2eef3));
    voiceEditor.BorderBrush(MakeBrush(0x315662));
    voiceEditor.TextChanged([this](auto &&, auto &&) { UpdateVoiceButtons(); });
}
Grid CreateVoiceFooter()
{
    Grid footer;
    footer.ColumnDefinitions().Append(ColumnDefinition());
    ColumnDefinition sendColumn;
    sendColumn.Width({1, GridUnitType::Auto});
    footer.ColumnDefinitions().Append(sendColumn);
    voiceCount.VerticalAlignment(VerticalAlignment::Center);
    footer.Children().Append(voiceCount);
    StackPanel controls;
    controls.Orientation(Orientation::Horizontal);
    Grid::SetColumn(controls, 1);
    voiceClear = DetailButton(L"清空");
    voiceClear.Padding({10, 4, 10, 4});
    voiceClear.Margin({0, 0, 6, 0});
    voiceClear.Click(
        [this](auto &&, auto &&)
        {
            voiceEditor.Text(L"");
            SetVoiceHint(L"");
        });
    controls.Children().Append(voiceClear);
    voiceSend = DetailButton(L"确认发送");
    voiceSend.Padding({12, 4, 12, 4});
    voiceSend.Background(MakeBrush(0x007f8f));
    voiceSend.Click([this](auto &&, auto &&) { ConfirmVoice(); });
    controls.Children().Append(voiceSend);
    footer.Children().Append(controls);
    return footer;
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
    hstring recordLabel = VoiceRecordLabel();
    auto label = voiceRecord.Content().try_as<TextBlock>();
    if (label && label.Text() != recordLabel)
        label.Text(recordLabel);
    voiceSend.IsEnabled(available && !voiceRunning && ConfirmedVoiceText(voiceEditor.Text(), text));
    voiceClear.IsEnabled(!voiceRunning && !chatPending && !chatCleanup && !voiceEditor.Text().empty());
    voiceClose.IsEnabled(!chatPending && !chatCleanup);
    voiceCount.Text(to_hstring(voiceEditor.Text().size()) + L" / 180");
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
    UpdateVoiceButtons();
    voiceEditor.Focus(FocusState::Programmatic);
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
    std::wstring id;
    for (wchar_t c : NewDetailId())
        if (c != L'{' && c != L'}' && c != L'-')
            id.push_back(c >= L'A' && c <= L'F' ? c + 32 : c);
    ++voiceGeneration;
    voiceId = hstring(id);
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
    JsonObject data;
    data.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    data.Insert(L"id", JsonValue::CreateStringValue(voiceId));
    data.Insert(L"session", JsonValue::CreateStringValue(voiceSession));
    data.Insert(L"createdAt", JsonValue::CreateNumberValue(double(voiceCreated)));
    data.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(Now())));
    data.Insert(L"action", JsonValue::CreateStringValue(voiceAction));
    data.Insert(L"ack", JsonValue::CreateNumberValue(double(voiceAck)));
    pendingVoiceWrite = data.Stringify();
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
    auto age = Now() - data.GetNamedNumber(L"updatedAt", 0);
    auto sequence = data.GetNamedNumber(L"sequence", 0);
    if (!voiceRunning || !VoiceCurrent(generation) || id != voiceId || data.GetNamedNumber(L"protocol", 0) != 1 ||
        data.GetNamedString(L"id", L"") != id || data.GetNamedString(L"session", L"") != voiceSession || age < 0 ||
        age >= 5000 || sequence <= double(voiceAck) || sequence > 1000000)
        return false;
    voiceAck = static_cast<int64_t>(sequence);
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
