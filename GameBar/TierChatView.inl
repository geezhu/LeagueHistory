// Included inside WidgetView. Only explicit clicks create bounded roster commands.
    StackPanel chatFooter;
    Button sendTier,sendMastery,cancelChat;
    std::array<MenuFlyoutItem,3> tierTargets,masteryTargets;
    TextBlock chatStatus=MakeText(L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。",12,0x91acbb);
    bool supportsChat=false,supportsMasteryChat=false,chatTeams=false,chatPending=false,chatWriting=false,chatReading=false,chatDismissed=false,chatCancelled=false;
    hstring chatId,chatSession,chatTarget,chatKind,pendingChatWrite;
    int chatSlot=-1,chatHero=0;
    int64_t chatCreated=0;

    void InitializeChat()
    {
        StackPanel actions;actions.Orientation(Orientation::Horizontal);
        sendTier=DetailButton(L"发送 Tier ▾");sendMastery=DetailButton(L"发送熟练度 ▾");cancelChat=DetailButton(L"取消发送");
        for(auto b:{sendTier,sendMastery,cancelChat}){b.Margin({0,0,6,0});actions.Children().Append(b);}
        auto menu=[this](Button button,std::array<MenuFlyoutItem,3>& items,hstring kind)
        {
            MenuFlyout flyout;
            std::array<hstring,3> names{L"我方全部英雄",L"敌方全部英雄",L"当前选中的英雄"};
            std::array<hstring,3> targets{L"own",L"enemy",L"single"};
            for(int i=0;i<3;++i){items[i].Text(names[i]);items[i].Click([this,kind,target=targets[i]](auto&&,auto&&){QueueChat(target,kind);});flyout.Items().Append(items[i]);}
            button.Flyout(flyout);
        };
        menu(sendTier,tierTargets,L"tier");menu(sendMastery,masteryTargets,L"mastery");
        cancelChat.Click([this](auto&&,auto&&){if(chatPending){chatCancelled=true;chatStatus.Text(L"正在取消发送…");SaveChatRequest();UpdateChatButtons();}});
        chatFooter.Margin({0,8,0,0});chatFooter.Children().Append(actions);
        chatStatus.TextWrapping(TextWrapping::Wrap);chatStatus.Margin({0,5,0,0});chatFooter.Children().Append(chatStatus);
        Grid::SetRow(chatFooter,4);root.Children().Append(chatFooter);UpdateChatButtons();
        ToolTipService::SetToolTip(sendTier,box_value(L"选择我方、敌方或当前选中英雄，发送梯队、胜率和选用率。"));
        ToolTipService::SetToolTip(sendMastery,box_value(L"选择我方、敌方或当前选中英雄，发送该玩家的英雄成就等级和点数。发送到当前游戏聊天频道；缓存和不可用数据会标注。"));
    }
    void UpdateChatButtons()
    {
        bool available=widget && !fixturePreview && supportsChat && enabled && active && !chatPending && Now()-lastGood<=15000;
        bool single=selectedCard>=0 && cards[selectedCard].hero>0;
        sendTier.IsEnabled(available && (chatTeams || single));sendMastery.IsEnabled(available && supportsMasteryChat && (chatTeams || single));
        sendMastery.Visibility(supportsMasteryChat?Visibility::Visible:Visibility::Collapsed);
        for(int i=0;i<3;++i){bool scope=i==2?single:chatTeams;tierTargets[i].IsEnabled(available && scope);masteryTargets[i].IsEnabled(available && supportsMasteryChat && scope);}
        cancelChat.Visibility(chatPending?Visibility::Visible:Visibility::Collapsed);cancelChat.IsEnabled(!chatCancelled);
    }
    void ResetChat()
    {
        chatPending=false;chatId=L"";pendingChatWrite=L"";
        chatStatus.Text(L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。");UpdateChatButtons();
    }
    void QueueChat(hstring const& target,hstring const& kind)
    {
        if(!widget || fixturePreview || !supportsChat || chatPending || !enabled || !active || Now()-lastGood>15000)return;
        if((kind!=L"tier" && kind!=L"mastery") || (kind==L"mastery" && !supportsMasteryChat))return;
        if(target==L"single" && (selectedCard<0 || cards[selectedCard].hero<=0))return;
        if(target!=L"single" && !chatTeams)return;
        std::wstring id;for(wchar_t c:NewDetailId())if(c!=L'{' && c!=L'}' && c!=L'-')id.push_back(c>=L'A' && c<=L'F'?c+32:c);
        chatId=hstring(id);chatSession=session;chatTarget=target;chatKind=kind;chatCreated=Now();chatCancelled=false;chatPending=true;
        chatSlot=target==L"single"?selectedCard:-1;chatHero=chatSlot>=0?cards[chatSlot].hero:0;
        auto name=target==L"own"?L"我方英雄":target==L"enemy"?L"敌方英雄":cards[chatSlot].champion.Text();
        chatStatus.Text(L"待发送："+hstring(name)+(kind==L"mastery"?L"熟练度":L"梯队")+L"。请按 Win＋G 返回游戏，保持聊天输入框关闭（20 秒内）。");
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
        catch(...){chatPending=false;pendingChatWrite=L"";chatStatus.Text(L"发送请求未能写入，请重试。");}
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
                if(data.GetNamedBoolean(L"complete",false))chatPending=false;
            }
        }
        catch(...){ }
        if(!stopped && chatPending && Now()-chatCreated>32000){chatPending=false;chatStatus.Text(L"发送请求已超时，请确认主程序正在运行后重试。");}
        chatReading=false;UpdateChatButtons();
    }
