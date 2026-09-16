// Included inside WidgetView. Only explicit clicks create bounded roster commands.
    StackPanel chatFooter;
    Button sendOwn,sendEnemy,sendSingle,cancelChat;
    TextBlock chatStatus=MakeText(L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。",12,0x91acbb);
    bool supportsChat=false,chatTeams=false,chatPending=false,chatWriting=false,chatReading=false,chatDismissed=false,chatCancelled=false;
    hstring chatId,chatSession,chatTarget,pendingChatWrite;
    int chatSlot=-1,chatHero=0;
    int64_t chatCreated=0;

    void InitializeChat()
    {
        StackPanel actions;actions.Orientation(Orientation::Horizontal);
        sendOwn=DetailButton(L"发送我方 Tier");sendEnemy=DetailButton(L"发送敌方 Tier");sendSingle=DetailButton(L"发送此英雄");cancelChat=DetailButton(L"取消发送");
        for(auto b:{sendOwn,sendEnemy,sendSingle,cancelChat}){b.Margin({0,0,6,0});actions.Children().Append(b);}
        sendOwn.Click([this](auto&&,auto&&){QueueChat(L"own");});
        sendEnemy.Click([this](auto&&,auto&&){QueueChat(L"enemy");});
        sendSingle.Click([this](auto&&,auto&&){QueueChat(L"single");});
        cancelChat.Click([this](auto&&,auto&&){if(chatPending){chatCancelled=true;chatStatus.Text(L"正在取消发送…");SaveChatRequest();UpdateChatButtons();}});
        chatFooter.Margin({0,8,0,0});chatFooter.Children().Append(actions);
        chatStatus.TextWrapping(TextWrapping::Wrap);chatStatus.Margin({0,5,0,0});chatFooter.Children().Append(chatStatus);
        Grid::SetRow(chatFooter,4);root.Children().Append(chatFooter);UpdateChatButtons();
        ToolTipService::SetToolTip(sendSingle,box_value(L"点击一张英雄卡片后，发送该英雄的梯队、胜率和选用率。"));
        ToolTipService::SetToolTip(sendEnemy,box_value(L"发送敌方英雄统计到当前游戏聊天频道，不会切换为所有人频道。"));
    }
    void UpdateChatButtons()
    {
        bool available=widget && !fixturePreview && supportsChat && enabled && active && !chatPending && Now()-lastGood<=15000;
        sendOwn.IsEnabled(available && chatTeams);sendEnemy.IsEnabled(available && chatTeams);
        sendSingle.IsEnabled(available && selectedCard>=0 && cards[selectedCard].hero>0);
        cancelChat.Visibility(chatPending?Visibility::Visible:Visibility::Collapsed);cancelChat.IsEnabled(!chatCancelled);
    }
    void ResetChat()
    {
        chatPending=false;chatId=L"";pendingChatWrite=L"";
        chatStatus.Text(L"发送到游戏聊天 · 需以管理员身份运行 League · 点击后按 Win＋G 返回游戏。");UpdateChatButtons();
    }
    void QueueChat(hstring const& target)
    {
        if(!widget || fixturePreview || !supportsChat || chatPending || !enabled || !active || Now()-lastGood>15000)return;
        if(target==L"single" && (selectedCard<0 || cards[selectedCard].hero<=0))return;
        if(target!=L"single" && !chatTeams)return;
        std::wstring id;for(wchar_t c:NewDetailId())if(c!=L'{' && c!=L'}' && c!=L'-')id.push_back(c>=L'A' && c<=L'F'?c+32:c);
        chatId=hstring(id);chatSession=session;chatTarget=target;chatCreated=Now();chatCancelled=false;chatPending=true;
        chatSlot=target==L"single"?selectedCard:-1;chatHero=chatSlot>=0?cards[chatSlot].hero:0;
        auto name=target==L"own"?L"我方英雄":target==L"enemy"?L"敌方英雄":cards[chatSlot].champion.Text();
        chatStatus.Text(L"待发送："+hstring(name)+L"。请按 Win＋G 返回游戏，保持聊天输入框关闭（20 秒内）。");
        SaveChatRequest();UpdateChatButtons();
    }
    void SaveChatRequest()
    {
        if(!widget || fixturePreview || !chatPending || stopped)return;
        chatDismissed=widget.GameBarDisplayMode()==XboxGameBarDisplayMode::PinnedOnly;
        JsonObject request;
        request.Insert(L"protocol",JsonValue::CreateNumberValue(1));request.Insert(L"id",JsonValue::CreateStringValue(chatId));
        request.Insert(L"session",JsonValue::CreateStringValue(chatSession));request.Insert(L"target",JsonValue::CreateStringValue(chatTarget));
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
