// The mini wave widget's voice row: one button records, stops or re-records, the other sends the recognized text
// or cancels a pending send. The text is shown but not editable, and nothing is sent without the send button. It
// uses the same files as the roster widget's voice row, so one recording and one send run at a time.
#pragma once
#include "VoiceProtocol.h"

struct MiniVoiceSender : std::enable_shared_from_this<MiniVoiceSender>
{
    enum class State
    {
        Idle,
        Preparing,
        Listening,
        Stopping,
        Decoding,
        Recognized,
        Sending,
        Cancelling,
    };

    Grid row;
    Button record, send;
    TextBlock text = MakeText(L"", 12, 0xe2eef3);
    XboxGameBarWidget widget{nullptr};
    State state = State::Idle;
    // brief is the one or two words the row shows; hint keeps the full reason for the tooltip.
    hstring recognized, brief, hint, session;
    hstring voiceId, voiceSession, voiceAction, chatId, chatSession;
    int64_t voiceCreated = 0, voiceAck = 0, chatCreated = 0;
    bool available = false, stopped = false, chatDismissed = false, voiceReading = false, chatReading = false;
    // A failed send shows its reason instead of the kept text until the next action.
    bool sendFailed = false;
    bool voiceWriting = false, chatWriting = false, voiceCleanup = false, chatCleanup = false;
    hstring pendingVoiceWrite, pendingChatWrite;

    MiniVoiceSender()
    {
        ColumnDefinition recordColumn, textColumn, sendColumn;
        recordColumn.Width({1, GridUnitType::Auto});
        sendColumn.Width({1, GridUnitType::Auto});
        for (auto const &column : {recordColumn, textColumn, sendColumn})
            row.ColumnDefinitions().Append(column);
        row.Margin({0, 4, 0, 0});
        record.Padding({8, 3, 8, 3});
        record.BorderThickness({0});
        record.Click([this](auto &&, auto &&) { OnRecord(); });
        row.Children().Append(record);
        text.VerticalAlignment(VerticalAlignment::Center);
        text.Margin({8, 0, 8, 0});
        Grid::SetColumn(text, 1);
        row.Children().Append(text);
        send.Padding({12, 3, 12, 3});
        send.BorderThickness({0});
        send.Click([this](auto &&, auto &&) { OnSend(); });
        Grid::SetColumn(send, 2);
        row.Children().Append(send);
        Refresh();
    }
    bool Busy() const
    {
        return state != State::Idle && state != State::Recognized;
    }
    bool Recording() const
    {
        return state == State::Preparing || state == State::Listening || state == State::Stopping ||
               state == State::Decoding;
    }
    // Called with every snapshot: losing the connection or changing game ends a recording or send in progress.
    void Update(bool value, hstring const &nextSession)
    {
        bool changed = session != nextSession;
        available = value;
        session = nextSession;
        if ((!available || changed) && state != State::Idle)
        {
            Cancel();
            SetHint(L"", L"");
        }
        Refresh();
    }
    void Tick()
    {
        if (stopped)
            return;
        if (Recording())
            ReadVoiceStatus();
        else if (state == State::Sending || state == State::Cancelling)
            ReadChatStatus();
    }
    // Leaving Game Bar is the main program's signal to type into the game.
    void DisplayModeChanged()
    {
        if (state == State::Sending)
            SaveChatRequest();
    }
    void Stop()
    {
        if (state != State::Idle)
            Cancel();
        stopped = true;
    }
    void Preview(hstring const &value)
    {
        recognized = value;
        state = State::Recognized;
        available = true;
        Refresh();
    }

    void OnRecord()
    {
        if (state == State::Listening)
        {
            state = State::Stopping;
            voiceAction = L"stop";
            SaveVoiceRequest();
        }
        else if (state == State::Idle || state == State::Recognized)
            StartRecording();
        Refresh();
    }
    void OnSend()
    {
        if (state == State::Sending)
        {
            state = State::Cancelling;
            SaveChatRequest();
            Trace(L"wave-mini-voice-send-cancel");
        }
        else if (state == State::Recognized)
            QueueSend();
        Refresh();
    }
    void StartRecording()
    {
        if (!available || voiceCleanup || chatCleanup || !widget)
            return;
        voiceId = NewRequestId();
        voiceSession = session;
        voiceCreated = Now();
        voiceAck = 0;
        voiceAction = L"start";
        recognized = L"";
        SetHint(L"", L"");
        sendFailed = false;
        state = State::Preparing;
        SaveVoiceRequest();
        Trace(L"wave-mini-voice-start");
    }
    void QueueSend()
    {
        hstring confirmed;
        if (!available || chatCleanup || !widget || !ConfirmedVoiceText(recognized, confirmed))
            return;
        chatId = NewRequestId();
        chatSession = session;
        chatCreated = Now();
        recognized = confirmed;
        SetHint(L"回游戏发送", L"请在 20 秒内按 Win＋G 返回游戏，保持聊天输入框关闭。");
        sendFailed = false;
        state = State::Sending;
        SaveChatRequest();
        Trace(L"wave-mini-voice-send");
    }
    // Ends whatever is running: an explicit cancel lets the main program release the microphone or drop the send.
    void Cancel()
    {
        if (Recording())
        {
            voiceAction = L"cancel";
            SaveVoiceRequest();
            ForgetRecording(true);
        }
        else if (state == State::Sending || state == State::Cancelling)
        {
            state = State::Cancelling;
            SaveChatRequest();
            ForgetSend();
        }
        state = State::Idle;
        recognized = L"";
    }

    void SaveVoiceRequest()
    {
        if (voiceId.empty())
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
                co_await WriteBridgeFileAsync(L"voice-request.tmp", L"voice-request.json", value);
            }
        }
        catch (hresult_error const &e)
        {
            pendingVoiceWrite = L"";
            if (Recording())
            {
                state = State::Idle;
                SetHint(L"请重试", L"录音请求未能同步，请重试。");
            }
            Trace(L"wave-mini-voice-write-error " + e.message());
        }
        voiceWriting = false;
        if (!stopped)
            Refresh();
    }
    fire_and_forget ReadVoiceStatus()
    {
        if (voiceReading || voiceCleanup)
            co_return;
        auto lifetime = shared_from_this();
        auto id = voiceId;
        voiceReading = true;
        // Rewriting the request renews the main program's 3-second lease on the microphone.
        SaveVoiceRequest();
        try
        {
            auto data = co_await ReadBridgeFileAsync(L"voice-status.json", 8192);
            if (!stopped && id == voiceId && Recording())
                ApplyVoiceStatus(data);
        }
        catch (...)
        {
            // The main process replaces status files atomically; a missing or partial reply is retried next tick.
        }
        if (!stopped && id == voiceId && Recording() && Now() - voiceCreated > VoiceTimeoutMilliseconds)
        {
            voiceAction = L"cancel";
            SaveVoiceRequest();
            ForgetRecording(true);
            state = State::Idle;
            SetHint(L"超时", L"录音响应超时，请确认 League 正在运行后重新识别。");
            Trace(L"wave-mini-voice-timeout");
        }
        voiceReading = false;
        if (!stopped)
            Refresh();
    }
    void ApplyVoiceStatus(JsonObject const &data)
    {
        if (!VoiceStatusAccepted(data, voiceId, voiceSession, voiceAck))
            return;
        voiceAck = static_cast<int64_t>(data.GetNamedNumber(L"sequence", 0));
        recognized = ClipVoice(std::wstring(data.GetNamedString(L"text", L"")), VoiceTextLimit);
        auto next = data.GetNamedString(L"state", L"");
        bool complete = data.GetNamedBoolean(L"complete", false);
        auto detail =
            next == L"recording" || next == L"done" || next == L"error" ? data.GetNamedString(L"hint", L"") : hstring();
        SetHint(next == L"error"                 ? L"识别失败"
                : next == L"recording"           ? L"请说话"
                : complete && recognized.empty() ? L"没听清"
                                                 : L"",
                detail);
        if (complete)
        {
            state = State::Recognized;
            ForgetRecording(false);
            Trace(L"wave-mini-voice-complete");
            return;
        }
        state = next == L"recording"  ? State::Listening
                : next == L"decoding" ? State::Decoding
                : next == L"stopping" ? State::Stopping
                                      : State::Preparing;
        SaveVoiceRequest();
    }
    // Deletes this recording's files once the writer is idle; a cancel first gives the main 400ms poll time to see it.
    fire_and_forget ForgetRecording(bool cancelled)
    {
        if (voiceCleanup || voiceId.empty())
            co_return;
        auto lifetime = shared_from_this();
        apartment_context ui;
        auto id = voiceId;
        voiceCleanup = true;
        while (voiceWriting)
        {
            co_await resume_after(std::chrono::milliseconds(50));
            co_await ui;
        }
        if (cancelled)
        {
            co_await resume_after(std::chrono::milliseconds(600));
            co_await ui;
        }
        co_await DeleteBridgeFileIfOwnedAsync(L"voice-request.json", id, 8192);
        co_await DeleteBridgeFileIfOwnedAsync(L"voice-status.json", id, 8192);
        if (voiceId == id)
            voiceId = L"";
        voiceCleanup = false;
        if (!stopped)
            Refresh();
    }

    void SaveChatRequest()
    {
        if (chatId.empty() || !widget)
            return;
        chatDismissed = widget.GameBarDisplayMode() == XboxGameBarDisplayMode::PinnedOnly;
        pendingChatWrite = VoiceChatRequestJson(
                               chatId, chatSession, recognized, chatCreated, chatDismissed, state == State::Cancelling)
                               .Stringify();
        WriteChatRequest();
    }
    fire_and_forget WriteChatRequest()
    {
        if (chatWriting)
            co_return;
        auto lifetime = shared_from_this();
        chatWriting = true;
        try
        {
            while (!pendingChatWrite.empty())
            {
                auto value = pendingChatWrite;
                pendingChatWrite = L"";
                co_await WriteBridgeFileAsync(L"chat-request.tmp", L"chat-request.json", value);
            }
        }
        catch (hresult_error const &e)
        {
            pendingChatWrite = L"";
            if (state == State::Sending || state == State::Cancelling)
            {
                state = State::Recognized;
                SetHint(L"请重试", L"发送请求未能写入，请重试。");
                sendFailed = true;
            }
            Trace(L"wave-mini-voice-send-write-error " + e.message());
        }
        chatWriting = false;
        if (!stopped)
            Refresh();
    }
    fire_and_forget ReadChatStatus()
    {
        if (chatReading || !widget)
            co_return;
        auto lifetime = shared_from_this();
        auto id = chatId;
        chatReading = true;
        if ((widget.GameBarDisplayMode() == XboxGameBarDisplayMode::PinnedOnly) != chatDismissed)
            SaveChatRequest();
        try
        {
            auto data = co_await ReadBridgeFileAsync(L"chat-status.json", 8192);
            if (!stopped && id == chatId && ChatStatusMatches(data, chatId, chatSession))
                ApplyChatStatus(data);
        }
        catch (...)
        {
            // An atomic bridge update may briefly remove the reply; keep polling until completion or timeout.
        }
        if (!stopped && id == chatId && (state == State::Sending || state == State::Cancelling) &&
            Now() - chatCreated > ChatTimeoutMilliseconds)
        {
            state = State::Recognized;
            SetHint(L"超时", L"发送请求已超时，请确认 League 正在运行后重试。");
            sendFailed = true;
            ForgetSend();
            Trace(L"wave-mini-voice-send-timeout");
        }
        chatReading = false;
        if (!stopped)
            Refresh();
    }
    // The main program's message goes to the tooltip; a delivered line clears the row, anything else keeps the text
    // so the player can send it again.
    void ApplyChatStatus(JsonObject const &data)
    {
        auto message = data.GetNamedString(L"message", L"");
        auto result = data.GetNamedString(L"state", L"");
        if (!data.GetNamedBoolean(L"complete", false))
        {
            SetHint(result == L"waiting" ? L"回游戏发送" : L"发送中…", message);
            return;
        }
        SetHint(result == L"input-complete" ? L"已发送" : result == L"cancelled" ? L"已取消" : L"未发送", message);
        Trace(L"wave-mini-voice-send-complete state=" + result);
        if (result == L"input-complete")
        {
            state = State::Idle;
            recognized = L"";
        }
        else
        {
            state = State::Recognized;
            sendFailed = true;
        }
        ForgetSend();
    }
    fire_and_forget ForgetSend()
    {
        if (chatCleanup || chatId.empty())
            co_return;
        auto lifetime = shared_from_this();
        apartment_context ui;
        auto id = chatId;
        chatCleanup = true;
        while (chatWriting)
        {
            co_await resume_after(std::chrono::milliseconds(100));
            co_await ui;
        }
        co_await DeleteBridgeFileIfOwnedAsync(L"chat-request.json", id, 4096);
        if (chatId == id)
            chatId = L"";
        chatCleanup = false;
        if (!stopped)
            Refresh();
    }

    // The record button reads 识别, 停止 or 重新识别; the send button 发送 or 取消 while a send waits.
    void Refresh()
    {
        bool listening = state == State::Listening;
        Symbol icon = listening                                           ? Symbol::Stop
                      : Recording()                                       ? Symbol::More
                      : state == State::Recognized && !recognized.empty() ? Symbol::Refresh
                                                                          : Symbol::Microphone;
        hstring label = listening                                           ? L"停止"
                        : Recording()                                       ? L"识别中…"
                        : state == State::Recognized && !recognized.empty() ? L"重新识别"
                                                                            : L"识别";
        record.Content(SymbolIcon(icon));
        record.Background(MakeBrush(listening ? 0x7a2c2c : 0x143542));
        ToolTipService::SetToolTip(record, box_value(label));
        Windows::UI::Xaml::Automation::AutomationProperties::SetName(record, label);
        record.IsEnabled(available && !voiceCleanup && !chatCleanup &&
                         (listening || state == State::Idle || state == State::Recognized));
        hstring confirmed;
        bool pending = state == State::Sending || state == State::Cancelling;
        send.Content(MakeText(pending ? L"取消" : L"发送", 12));
        send.Background(MakeBrush(pending ? 0x5a3a2a : 0x007f8f));
        send.IsEnabled(available && (state == State::Sending || (state == State::Recognized && !chatCleanup &&
                                                                 ConfirmedVoiceText(recognized, confirmed))));
        RefreshText();
    }
    void SetHint(hstring const &shortText, hstring const &detail)
    {
        brief = shortText;
        hint = detail;
    }
    // The row shows the recognized text or one or two words of status; the tooltip has the full text and reason.
    void RefreshText()
    {
        hstring value;
        uint32_t color = 0x91acbb;
        switch (state)
        {
        case State::Idle:
            value = brief;
            break;
        case State::Preparing:
            value = L"准备中…";
            break;
        case State::Listening:
            value = !recognized.empty() ? recognized : hstring(L"请说话");
            color = recognized.empty() ? 0x7ff0ee : 0xe2eef3;
            break;
        case State::Stopping:
        case State::Decoding:
            value = recognized.empty() ? hstring(L"识别中…") : recognized;
            break;
        case State::Recognized:
            value = sendFailed || recognized.empty() ? brief : recognized;
            color = sendFailed || recognized.empty() ? 0xdbc47f : 0xe2eef3;
            break;
        case State::Sending:
        case State::Cancelling:
            value = brief;
            color = 0xdbc47f;
            break;
        }
        text.Text(value);
        text.Foreground(MakeBrush(color));
        hstring tip = recognized.empty() ? hint : (hint.empty() ? recognized : recognized + L"\n" + hint);
        ToolTipService::SetToolTip(text, tip.empty() ? nullptr : box_value(tip));
    }
};
