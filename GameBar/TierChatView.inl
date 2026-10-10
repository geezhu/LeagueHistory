// Included inside WidgetView. Statistics and confirmed voice text share the send gate.
    StackPanel chatFooter;
    Button sendTier,sendMastery,sendDeathStreak,cancelChat;
    std::array<MenuFlyoutItem,3> tierTargets,masteryTargets,deathStreakTargets;
    TextBlock chatStatus=MakeText(L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。",12,0x91acbb);
    bool supportsChat=false,supportsMasteryChat=false,supportsDeathStreakChat=false,chatTeams=false,chatPending=false,chatWriting=false,chatReading=false,chatDismissed=false,chatCancelled=false,chatCleanup=false;
    hstring chatId,chatSession,chatTarget,chatKind,chatText,pendingChatWrite;
    int chatSlot=-1,chatHero=0;
    int64_t chatCreated=0;

    void InitializeChat()
    {
        StackPanel actions;actions.Orientation(Orientation::Horizontal);
        sendTier=DetailButton(L"发送 Tier ▾");sendMastery=DetailButton(L"发送熟练度 ▾");sendDeathStreak=DetailButton(L"发送连死 ▾");cancelChat=DetailButton(L"取消发送");
        for(auto b:{sendTier,sendMastery,sendDeathStreak,cancelChat}){b.Margin({0,0,6,0});actions.Children().Append(b);}
        InitializeVoice();actions.Children().Append(voiceButton);
        auto menu=[this](Button button,std::array<MenuFlyoutItem,3>& items,hstring kind)
        {
            MenuFlyout flyout;
            std::array<hstring,3> names{L"我方全部英雄",L"敌方全部英雄",L"当前选中的英雄"};
            std::array<hstring,3> targets{L"own",L"enemy",L"single"};
            for(int i=0;i<3;++i){items[i].Text(names[i]);items[i].Click([this,kind,target=targets[i]](auto&&,auto&&){QueueChat(target,kind);});flyout.Items().Append(items[i]);}
            button.Flyout(flyout);
        };
        menu(sendTier,tierTargets,L"tier");menu(sendMastery,masteryTargets,L"mastery");menu(sendDeathStreak,deathStreakTargets,L"deathStreak");
        cancelChat.Click([this](auto&&,auto&&){if(chatPending){chatCancelled=true;chatStatus.Text(L"正在取消发送…");SaveChatRequest();UpdateChatButtons();}});
        chatFooter.Margin({0,8,0,0});chatFooter.Children().Append(actions);
        chatFooter.Children().Append(voicePanel);
        chatStatus.TextWrapping(TextWrapping::Wrap);chatStatus.Margin({0,5,0,0});chatFooter.Children().Append(chatStatus);
        Grid::SetRow(chatFooter,4);root.Children().Append(chatFooter);UpdateChatButtons();
        ToolTipService::SetToolTip(sendTier,box_value(L"选择我方、敌方或当前选中英雄，发送梯队、胜率和选用率。"));
        ToolTipService::SetToolTip(sendDeathStreak,box_value(L"发送当前连死和本局最高次数。死亡加一，击杀清零，助攻不清零；不完整历史标注已记录，无法匹配的玩家显示未知。"));
        ToolTipService::SetToolTip(sendMastery,box_value(L"选择我方、敌方或当前选中英雄，发送该玩家的英雄成就等级和点数。发送到当前游戏聊天频道；缓存和不可用数据会标注。"));
    }
    void UpdateChatButtons()
    {
        bool available=widget && !fixturePreview && supportsChat && enabled && active && !chatPending && !chatCleanup && !voiceRunning && !voiceCleanup && Now()-lastGood<=15000;
        bool single=selectedCard>=0 && cards[selectedCard].hero>0;
        sendTier.IsEnabled(available && (chatTeams || single));sendMastery.IsEnabled(available && supportsMasteryChat && (chatTeams || single));
        sendMastery.Visibility(supportsMasteryChat?Visibility::Visible:Visibility::Collapsed);
        sendDeathStreak.IsEnabled(available && supportsDeathStreakChat && (chatTeams || single));
        sendDeathStreak.Visibility(supportsDeathStreakChat?Visibility::Visible:Visibility::Collapsed);
        for(int i=0;i<3;++i){bool scope=i==2?single:chatTeams;tierTargets[i].IsEnabled(available && scope);masteryTargets[i].IsEnabled(available && supportsMasteryChat && scope);deathStreakTargets[i].IsEnabled(available && supportsDeathStreakChat && scope);}
        cancelChat.Visibility(chatPending?Visibility::Visible:Visibility::Collapsed);cancelChat.IsEnabled(!chatCancelled);
        UpdateVoiceButtons();
    }
    void ResetChat()
    {
        if(chatKind==L"voice" && !chatId.empty())ForgetVoiceRequest();
        chatPending=false;chatId=L"";chatText=L"";pendingChatWrite=L"";
        chatStatus.Text(L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。");UpdateChatButtons();
    }
    void QueueChat(hstring const& target,hstring const& kind)
    {
        if(!widget || fixturePreview || !supportsChat || chatPending || chatCleanup || voiceRunning || voiceCleanup || !enabled || !active || Now()-lastGood>15000)return;
        if((kind!=L"tier" && kind!=L"mastery" && kind!=L"deathStreak" && kind!=L"voice") || (kind==L"mastery" && !supportsMasteryChat) || (kind==L"deathStreak" && !supportsDeathStreakChat))return;
        if(kind==L"voice" && (!supportsVoiceChat || target!=L"text" || chatText.empty()))return;
        if(target==L"single" && (selectedCard<0 || cards[selectedCard].hero<=0))return;
        if(kind!=L"voice" && target!=L"single" && !chatTeams)return;
        if(kind!=L"voice")chatText=L"";
        std::wstring id;for(wchar_t c:NewDetailId())if(c!=L'{' && c!=L'}' && c!=L'-')id.push_back(c>=L'A' && c<=L'F'?c+32:c);
        chatId=hstring(id);chatSession=session;chatTarget=target;chatKind=kind;chatCreated=Now();chatCancelled=false;chatPending=true;
        chatSlot=target==L"single"?selectedCard:-1;chatHero=chatSlot>=0?cards[chatSlot].hero:0;
        auto name=kind==L"voice"?L"已确认的文字":target==L"own"?L"我方英雄":target==L"enemy"?L"敌方英雄":cards[chatSlot].champion.Text();
        auto metric=kind==L"mastery"?L"熟练度":kind==L"deathStreak"?L"连死次数":L"梯队";
        chatStatus.Text(kind==L"voice"?L"文字已确认。请在 20 秒内按 Win＋G 返回游戏，保持聊天输入框关闭；可取消发送。":L"待发送："+hstring(name)+metric+L"。请按 Win＋G 返回游戏，保持聊天输入框关闭（20 秒内）。");
        SaveChatRequest();UpdateChatButtons();
    }
    void SaveChatRequest()
    {
        if(!widget || fixturePreview || !chatPending || stopped)return;
        chatDismissed=widget.GameBarDisplayMode()==XboxGameBarDisplayMode::PinnedOnly;
        JsonObject request;
        request.Insert(L"protocol",JsonValue::CreateNumberValue(1));request.Insert(L"id",JsonValue::CreateStringValue(chatId));
        request.Insert(L"session",JsonValue::CreateStringValue(chatSession));request.Insert(L"target",JsonValue::CreateStringValue(chatTarget));
        request.Insert(L"kind",JsonValue::CreateStringValue(chatKind));
        if(chatKind==L"voice"){request.Insert(L"text",JsonValue::CreateStringValue(chatText));request.Insert(L"confirmed",JsonValue::CreateBooleanValue(true));}
        request.Insert(L"slot",JsonValue::CreateNumberValue(chatSlot));request.Insert(L"championId",JsonValue::CreateNumberValue(chatHero));
        request.Insert(L"updatedAt",JsonValue::CreateNumberValue(double(chatCreated)));
        request.Insert(L"dismissed",JsonValue::CreateBooleanValue(chatDismissed));request.Insert(L"cancelled",JsonValue::CreateBooleanValue(chatCancelled));
        pendingChatWrite=request.Stringify();WriteChatRequest();
    }
    fire_and_forget WriteChatRequest()
    {
        if(chatWriting || stopped)co_return;
        auto lifetime=shared_from_this();chatWriting=true;
        try
        {
            while(!pendingChatWrite.empty() && !stopped)
            {
                auto value=pendingChatWrite;pendingChatWrite=L"";
                auto file=co_await ApplicationData::Current().LocalFolder().CreateFileAsync(L"chat-request.tmp",CreationCollisionOption::ReplaceExisting);
                co_await FileIO::WriteTextAsync(file,value);co_await file.RenameAsync(L"chat-request.json",NameCollisionOption::ReplaceExisting);
            }
        }
        catch(...){chatPending=false;pendingChatWrite=L"";chatStatus.Text(L"发送请求未能写入，请重试。");if(chatKind==L"voice")ForgetVoiceRequest();}
        chatWriting=false;UpdateChatButtons();
    }
    fire_and_forget ReadChatStatus()
    {
        if(stopped || fixturePreview || !widget || !chatPending || chatReading)co_return;
        auto lifetime=shared_from_this();chatReading=true;auto id=chatId;
        try
        {
            if((widget.GameBarDisplayMode()==XboxGameBarDisplayMode::PinnedOnly)!=chatDismissed)SaveChatRequest();
            auto file=co_await ApplicationData::Current().LocalFolder().GetFileAsync(L"chat-status.json");
            auto properties=co_await file.GetBasicPropertiesAsync();if(properties.Size()>8192)throw hresult_invalid_argument();
            auto text=co_await FileIO::ReadTextAsync(file);auto data=JsonObject::Parse(text);
            if(!stopped && id==chatId && data.GetNamedNumber(L"protocol",0)==1 && data.GetNamedString(L"requestId",L"")==chatId && data.GetNamedString(L"session",L"")==chatSession)
            {
                chatStatus.Text(data.GetNamedString(L"message",L"正在处理发送…"));
                if(data.GetNamedBoolean(L"complete",false))
                {
                    chatPending=false;
                    if(chatKind==L"voice")
                    {
                        if(data.GetNamedString(L"state",L"")==L"input-complete")voiceEditor.Text(L"");
                        SetVoiceHint(L"");ForgetVoiceRequest();
                    }
                }
            }
        }
        catch(...){ }
        if(!stopped && chatPending && Now()-chatCreated>32000){chatPending=false;chatStatus.Text(L"发送请求已超时，请确认主程序正在运行后重试。");if(chatKind==L"voice")ForgetVoiceRequest();}
        chatReading=false;UpdateChatButtons();
    }
    fire_and_forget ForgetVoiceRequest()
    {
        if(chatCleanup)co_return;
        auto lifetime=shared_from_this();apartment_context ui;auto id=chatId;chatCleanup=true;
        try
        {
            while(chatWriting){co_await resume_after(std::chrono::milliseconds(100));co_await ui;}
            auto file=co_await ApplicationData::Current().LocalFolder().GetFileAsync(L"chat-request.json");
            auto properties=co_await file.GetBasicPropertiesAsync();
            if(properties.Size()<=4096)
            {
                auto data=JsonObject::Parse(co_await FileIO::ReadTextAsync(file));
                if(data.GetNamedString(L"id",L"")==id && data.GetNamedString(L"kind",L"")==L"voice")co_await file.DeleteAsync();
            }
        }
        catch(...){ }
        chatText=L"";chatCleanup=false;if(!stopped)UpdateChatButtons();
    }
