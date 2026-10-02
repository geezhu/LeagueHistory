// Included inside WidgetView. The detail page stays in the Game Bar-owned view.
    Grid detailRoot;
    StackPanel detailBody, detailTabs;
    ScrollViewer detailScroll;
    TextBlock detailTitle = MakeText(L"英雄详情", 22, 0xe9d8a6);
    TextBlock detailStats = MakeText(L"", 13, 0x65d6d6);
    TextBlock detailMastery = MakeText(L"", 12, 0xdbc47f);
    TextBlock detailSource = MakeText(L"", 12, 0xa4bcc7);
    Image detailArtwork;
    std::array<Button, 10> heroButtons;
    std::array<Image, 10> heroIcons;
    std::array<Button, 4> tabButtons;
    JsonObject detailData;
    hstring detailRequest, detailRevision, detailTab = L"skills";
    hstring pendingDetailRequest;
    int64_t detailRequestedAt = 0;
    bool detailBusy = false, requestBusy = false, supportsDetails = false;
    bool fixturePreview = false, fixtureOpened = false;
    int pendingDetailImages = 0;
    int detailQueue = 0;
    int detailRarity = 0;
    uint64_t detailRender = 0;

    static hstring NewDetailId(){GUID value{};check_hresult(CoCreateGuid(&value));return to_hstring(guid(value));}
    void ConfigureDetailPreview(hstring const& argument)
    {
        std::wstring_view text(argument),prefix=L"--capture-detail-fixture:";
        if(!text.starts_with(prefix))return;
        auto tab=text.substr(prefix.size());
        if(tab!=L"skills" && tab!=L"builds" && tab!=L"runes" && tab!=L"augments" && tab!=L"roster")return;
        fixturePreview=true;fixtureOpened=false;Clear();revision=L"";detailTab=hstring(tab);capturePending=true;
    }

    static Button DetailButton(hstring const& text)
    {
        Button b; b.Content(MakeText(text, 13)); b.Background(MakeBrush(0x143542));
        b.BorderBrush(MakeBrush(0x315662)); b.BorderThickness({1}); b.Padding({12, 6, 12, 6});
        return b;
    }
    void InitializeDetails()
    {
        for(int i=0;i<4;++i) { RowDefinition r; r.Height({1,i==3?GridUnitType::Star:GridUnitType::Auto}); detailRoot.RowDefinitions().Append(r); }
        StackPanel navigation; navigation.Orientation(Orientation::Horizontal); navigation.Margin({0,0,0,8});
        auto back=DetailButton(L"‹ 返回"); back.Margin({0,0,8,0}); back.Click([this](auto&&,auto&&){ CloseDetails(true); }); navigation.Children().Append(back);
        for(int i=0;i<10;++i)
        {
            auto& b=heroButtons[i]; b=DetailButton(L""); b.Padding({2}); b.Margin({0,0,i==4?10.0:3.0,0});
            heroIcons[i].Width(32); heroIcons[i].Height(32); b.Content(heroIcons[i]);
            b.Click([this,i](auto&&,auto&&){ OpenDetails(i); }); navigation.Children().Append(b);
        }
        auto retry=DetailButton(L"重试"); retry.Margin({6,0,0,0}); retry.Click([this](auto&&,auto&&){ if(selectedCard>=0)OpenDetails(selectedCard); });
        navigation.Children().Append(retry); detailRoot.Children().Append(navigation);
        Grid banner; banner.Height(128); banner.Background(MakeBrush(0x102f3c));
        detailArtwork.Stretch(Stretch::UniformToFill); detailArtwork.Opacity(.36); banner.Children().Append(detailArtwork);
        StackPanel summary; summary.Margin({14,8,14,8});
        summary.Children().Append(detailTitle); detailStats.Margin({0,3,0,3}); summary.Children().Append(detailStats);
        summary.Children().Append(detailMastery);
        detailSource.TextWrapping(TextWrapping::Wrap); summary.Children().Append(detailSource); banner.Children().Append(summary);
        Grid::SetRow(banner,1); detailRoot.Children().Append(banner);
        detailTabs.Orientation(Orientation::Horizontal); detailTabs.Margin({0,8,0,8});
        std::array<hstring,4> names{L"技能",L"出装",L"符文",L"海克斯"};
        std::array<hstring,4> keys{L"skills",L"builds",L"runes",L"augments"};
        for(int i=0;i<4;++i)
        {
            auto& b=tabButtons[i]; b=DetailButton(names[i]); b.Margin({0,0,6,0}); b.MinWidth(80);
            b.Click([this,key=keys[i]](auto&&,auto&&){ detailTab=key; RenderDetails(); }); detailTabs.Children().Append(b);
        }
        Grid::SetRow(detailTabs,2);detailRoot.Children().Append(detailTabs);
        detailScroll.Content(detailBody); detailScroll.HorizontalScrollBarVisibility(ScrollBarVisibility::Disabled);
        detailScroll.VerticalScrollBarVisibility(ScrollBarVisibility::Auto); Grid::SetRow(detailScroll,3); detailRoot.Children().Append(detailScroll);
        Grid::SetRow(detailRoot,2); Grid::SetRowSpan(detailRoot,2); detailRoot.Visibility(Visibility::Collapsed);root.Children().Append(detailRoot);
    }
    void RefreshDetailHeader()
    {
        for(int i=0;i<10;++i)
        {
            heroIcons[i].Source(cards[i].icon.Source()); heroButtons[i].IsEnabled(cards[i].hero>0);
            heroButtons[i].BorderBrush(MakeBrush(i==selectedCard?0x43d8d7:0x315662));
            ToolTipService::SetToolTip(heroButtons[i],box_value(teams[i/5].Text()+L" · "+cards[i].champion.Text()));
        }
        if(selectedCard<0)return;
        detailTitle.Text(cards[selectedCard].champion.Text()+L"  "+cards[selectedCard].tier.Text());
        detailStats.Text(cards[selectedCard].stats.Text());
        detailMastery.Text(cards[selectedCard].player.Text()+L" · "+cards[selectedCard].mastery.Text());
        ToolTipService::SetToolTip(detailMastery,box_value(cards[selectedCard].masteryDetails));
        ToolTipService::SetToolTip(detailStats,box_value(cards[selectedCard].details));
        bool mayhem=detailQueue==2400 || detailQueue==3270;
        tabButtons[2].Visibility(mayhem?Visibility::Collapsed:Visibility::Visible);
        tabButtons[3].Visibility(mayhem?Visibility::Visible:Visibility::Collapsed);
    }
    void OpenDetails(int index)
    {
        if(index<0 || index>=10 || cards[index].hero<=0)return;
        if(!supportsDetails){details.Text(L"当前主程序尚不支持英雄详情，请启动新版 League.exe。");return;}
        selectedCard=index; detailData=JsonObject();detailRevision=L"";
        detailRequest=NewDetailId();detailRequestedAt=Now();
        detailArtwork.Source(nullptr);detailSource.Text(L"正在读取英雄详情…");
        if((detailQueue==2400 || detailQueue==3270) && detailTab==L"runes")detailTab=L"augments";
        else if(detailQueue!=2400 && detailQueue!=3270 && detailTab==L"augments")detailTab=L"skills";
        rosterScroll.Visibility(Visibility::Collapsed); details.Visibility(Visibility::Collapsed); detailRoot.Visibility(Visibility::Visible);
        RefreshDetailHeader();RenderDetails(); QueueDetailRequest(index,cards[index].hero,detailRequest);
        UpdateChatButtons();
    }
    void CloseDetails(bool notify)
    {
        ++detailRender;selectedCard=-1;detailRequest=L"";detailRevision=L"";detailData=JsonObject();detailArtwork.Source(nullptr);
        detailBody.Children().Clear();detailRoot.Visibility(Visibility::Collapsed);rosterScroll.Visibility(Visibility::Visible);details.Visibility(Visibility::Visible);
        if(notify && supportsDetails)QueueDetailRequest(-1,0,NewDetailId());
        UpdateChatButtons();
    }
    void QueueDetailRequest(int slot,int hero,hstring id)
    {
        if(fixturePreview)return;
        JsonObject request;request.Insert(L"protocol",JsonValue::CreateNumberValue(1));request.Insert(L"id",JsonValue::CreateStringValue(id));
        request.Insert(L"session",JsonValue::CreateStringValue(session));request.Insert(L"slot",JsonValue::CreateNumberValue(slot));
        request.Insert(L"championId",JsonValue::CreateNumberValue(hero));request.Insert(L"updatedAt",JsonValue::CreateNumberValue(double(Now())));
        pendingDetailRequest=request.Stringify();WriteDetailRequest();
    }
    fire_and_forget WriteDetailRequest()
    {
        if(requestBusy || stopped)co_return;auto lifetime=shared_from_this();requestBusy=true;
        while(!pendingDetailRequest.empty() && !stopped)
        {
            auto json=pendingDetailRequest;pendingDetailRequest=L"";
            try
            {
                auto folder=ApplicationData::Current().LocalFolder();
                auto file=co_await folder.CreateFileAsync(L"detail-request.tmp",CreationCollisionOption::ReplaceExisting);
                co_await FileIO::WriteTextAsync(file,json);co_await file.RenameAsync(L"detail-request.json",NameCollisionOption::ReplaceExisting);
            }
            catch(hresult_error const&){detailSource.Text(L"无法请求英雄详情，请重试。");}
        }
        requestBusy=false;
    }
    fire_and_forget DecodeDetailImage(Image target,hstring data,uint64_t render,int width=48)
    {
        auto lifetime=shared_from_this();
        if(data.empty())co_return;
        ++pendingDetailImages;
        try
        {
            auto buffer=Windows::Security::Cryptography::CryptographicBuffer::DecodeFromBase64String(data);
            if(buffer.Length()>400000){--pendingDetailImages;co_return;}
            InMemoryRandomAccessStream stream;co_await stream.WriteAsync(buffer);stream.Seek(0);
            BitmapImage bitmap;bitmap.DecodePixelWidth(width);co_await bitmap.SetSourceAsync(stream);
            if(!stopped && render==detailRender)target.Source(bitmap);
        }
        catch(...){ }
        --pendingDetailImages;
    }
    Grid DetailIcons(JsonArray const& icons)
    {
        Grid grid;grid.HorizontalAlignment(HorizontalAlignment::Left);grid.Margin({0,5,0,5});
        for(int col=0;col<9;++col){ColumnDefinition c;c.Width({48,GridUnitType::Pixel});grid.ColumnDefinitions().Append(c);}
        for(uint32_t row=0;row<(icons.Size()+8)/9;++row){RowDefinition r;r.Height({48,GridUnitType::Pixel});grid.RowDefinitions().Append(r);}
        for(uint32_t i=0;i<icons.Size() && i<27;++i)
        {
            auto icon=icons.GetObjectAt(i);auto name=icon.GetNamedString(L"name",L"图标");
            Grid tile;tile.Background(MakeBrush(0x1b414e));tile.Margin({0,0,4,4});
            auto placeholder=MakeText(hstring(std::wstring_view(name).substr(0,std::min<size_t>(2,name.size()))),11,0x91acbb);placeholder.HorizontalAlignment(HorizontalAlignment::Center);placeholder.VerticalAlignment(VerticalAlignment::Center);tile.Children().Append(placeholder);
            Image image;image.Width(40);image.Height(40);tile.Children().Append(image);DecodeDetailImage(image,icon.GetNamedString(L"image",L""),detailRender);
            auto tip=MakeText(name+L"\n"+icon.GetNamedString(L"description",L"暂无说明"),13);tip.TextWrapping(TextWrapping::Wrap);tip.MaxWidth(380);
            ToolTip tooltip;tooltip.Content(tip);ToolTipService::SetToolTip(tile,tooltip);
            Grid::SetRow(tile,i/9);Grid::SetColumn(tile,i%9);grid.Children().Append(tile);
        }
        return grid;
    }
    void RenderDetails()
    {
        ++detailRender;detailBody.Children().Clear();detailScroll.ChangeView(nullptr,0.0,nullptr);
        std::array<hstring,4> keys{L"skills",L"builds",L"runes",L"augments"};
        for(int i=0;i<4;++i)tabButtons[i].Background(MakeBrush(keys[i]==detailTab?0x008b98:0x143542));
        if(detailTab==L"augments")
        {
            StackPanel rarities;rarities.Orientation(Orientation::Horizontal);rarities.Margin({0,0,0,6});
            std::array<hstring,3> names{L"棱彩",L"黄金",L"白银"};
            for(int i=0;i<3;++i){auto b=DetailButton(names[i]);b.Margin({0,0,6,0});b.Background(MakeBrush(i==detailRarity?0x3e375c:0x143542));b.Click([this,i](auto&&,auto&&){detailRarity=i;RenderDetails();});rarities.Children().Append(b);}
            detailBody.Children().Append(rarities);
        }
        bool found=false;
        int rarityIndex=0;
        for(auto const& entry:detailData.GetNamedArray(L"sections",JsonArray()))
        {
            auto section=entry.GetObject();if(section.GetNamedString(L"tab",L"")!=detailTab)continue;
            if(detailTab==L"augments" && rarityIndex++!=detailRarity)continue;found=true;
            auto title=MakeText(section.GetNamedString(L"title",L""),15,0xe0c688);title.Margin({0,6,0,6});detailBody.Children().Append(title);
            auto note=section.GetNamedString(L"note",L"");
            if(!note.empty()){auto text=MakeText(note,12,0x91acbb);text.TextWrapping(TextWrapping::Wrap);text.Margin({0,0,0,8});detailBody.Children().Append(text);}
            auto rows=section.GetNamedArray(L"rows",JsonArray());
            if(rows.Size()==0){auto empty=MakeText(L"来源暂无这一项数据。",13,0x91acbb);empty.Margin({10,6,0,10});detailBody.Children().Append(empty);}
            if((detailTab==L"augments" || section.GetNamedString(L"layout",L"")==L"equipment") && rows.Size()>0)
            {
                Grid tiles;
                for(int i=0;i<3;++i)tiles.ColumnDefinitions().Append(ColumnDefinition());
                for(uint32_t i=0;i<(rows.Size()+2)/3;++i){RowDefinition r;r.Height({1,GridUnitType::Auto});tiles.RowDefinitions().Append(r);}
                for(uint32_t i=0;i<rows.Size();++i)
                {
                    auto row=rows.GetObjectAt(i);auto icons=row.GetNamedArray(L"icons",JsonArray());if(icons.Size()==0)continue;auto icon=icons.GetObjectAt(0);
                    StackPanel card;card.Padding({8});card.Margin({0,0,6,6});card.Background(MakeBrush(0x112f3c));
                    auto name=MakeText(icon.GetNamedString(L"name",L"推荐"),13);card.Children().Append(name);
                    StackPanel metrics;metrics.Orientation(Orientation::Horizontal);metrics.Margin({0,6,0,0});Image image;image.Width(40);image.Height(40);image.Margin({0,0,6,0});
                    DecodeDetailImage(image,icon.GetNamedString(L"image",L""),detailRender);metrics.Children().Append(image);
                    StackPanel rates;rates.Children().Append(MakeText(row.GetNamedString(L"winRateText",L"胜率 —"),12,0x65d6d6));rates.Children().Append(MakeText(row.GetNamedString(L"pickRateText",L"选取率 —"),11,0xa4bcc7));metrics.Children().Append(rates);card.Children().Append(metrics);
                    auto text=MakeText(icon.GetNamedString(L"name",L"")+L"\n"+icon.GetNamedString(L"description",L"暂无说明")+L"\n"+row.GetNamedString(L"stats",L""),13);text.TextWrapping(TextWrapping::Wrap);text.MaxWidth(380);ToolTip tip;tip.Content(text);ToolTipService::SetToolTip(card,tip);
                    Grid::SetRow(card,i/3);Grid::SetColumn(card,i%3);tiles.Children().Append(card);
                }
                detailBody.Children().Append(tiles);continue;
            }
            if(detailTab==L"skills" && rows.Size()>0 && rows.GetObjectAt(0).GetNamedArray(L"icons",JsonArray()).Size()>0
                && rows.GetObjectAt(0).GetNamedArray(L"icons").GetObjectAt(0).GetNamedString(L"kind",L"")==L"skill")
            {
                StackPanel choices;choices.Orientation(Orientation::Horizontal);choices.Margin({0,2,0,10});
                auto description=MakeText(L"",14,0xc0d5df);description.TextWrapping(TextWrapping::Wrap);description.Margin({12,12,12,12});
                Border descriptionCard;descriptionCard.Background(MakeBrush(0x112f3c));descriptionCard.Child(description);
                for(uint32_t i=0;i<rows.Size() && i<5;++i)
                {
                    auto row=rows.GetObjectAt(i);auto icon=row.GetNamedArray(L"icons").GetObjectAt(0);
                    auto title=row.GetNamedString(L"title",L"");auto text=title+L"\n\n"+row.GetNamedString(L"description",L"暂无说明");
                    StackPanel content;Image image;image.Width(44);image.Height(44);DecodeDetailImage(image,icon.GetNamedString(L"image",L""),detailRender);content.Children().Append(image);
                    auto key=MakeText(icon.GetNamedString(L"key",L""),13,0xe0c688);key.HorizontalAlignment(HorizontalAlignment::Center);content.Children().Append(key);
                    auto button=DetailButton(L"");button.Content(content);button.MinWidth(72);button.Margin({0,0,8,0});button.Click([description,text](auto&&,auto&&){description.Text(text);});
                    auto tip=MakeText(text,13);tip.TextWrapping(TextWrapping::Wrap);tip.MaxWidth(380);ToolTip tooltip;tooltip.Content(tip);ToolTipService::SetToolTip(button,tooltip);
                    choices.Children().Append(button);if(i==0)description.Text(text);
                }
                detailBody.Children().Append(choices);detailBody.Children().Append(descriptionCard);continue;
            }
            for(auto const& value:rows)
            {
                auto row=value.GetObject();StackPanel body;body.Padding({12,8,12,8});body.Background(MakeBrush(0x112f3c));body.Margin({0,0,6,6});
                auto icons=row.GetNamedArray(L"icons",JsonArray());auto name=row.GetNamedString(L"title",L"");
                if(name.empty() && icons.Size()>0)name=icons.GetObjectAt(0).GetNamedString(L"name",L"");
                auto heading=MakeText(name,14);heading.TextWrapping(TextWrapping::Wrap);body.Children().Append(heading);
                if(icons.Size()>0)body.Children().Append(DetailIcons(icons));
                auto stats=row.GetNamedString(L"stats",L"");if(!stats.empty()){auto text=MakeText(stats,12,0x65d6d6);text.TextWrapping(TextWrapping::Wrap);body.Children().Append(text);}
                auto description=row.GetNamedString(L"description",L"");
                if(!description.empty()){auto text=MakeText(description,13,0xb4cad4);text.TextWrapping(TextWrapping::Wrap);text.Margin({0,5,0,2});body.Children().Append(text);}
                auto later=row.GetNamedArray(L"later",JsonArray());
                if(later.Size()>0)
                {
                    auto button=DetailButton(L"后续可选装备 ▾");button.Margin({0,6,0,0});auto choices=DetailIcons(later);choices.Visibility(Visibility::Collapsed);
                    button.Click([choices](auto&&,auto&&){choices.Visibility(choices.Visibility()==Visibility::Visible?Visibility::Collapsed:Visibility::Visible);});
                    body.Children().Append(button);body.Children().Append(choices);
                }
                detailBody.Children().Append(body);
            }
        }
        if(!found)
        {
            auto text=MakeText(detailData.GetNamedString(L"state",L"loading")==L"loading"?L"正在读取这一页的数据…":L"当前模式暂无这一项数据。",14,0x91acbb);
            text.Margin({12,20,12,20});text.TextWrapping(TextWrapping::Wrap);detailBody.Children().Append(text);
        }
        DecodeDetailImage(detailArtwork,detailData.GetNamedString(L"art",L""),detailRender,560);
    }
    fire_and_forget ReadDetails()
    {
        if(detailBusy || stopped || selectedCard<0 || detailRequest.empty())co_return;
        if(!detailRevision.empty() && detailData.GetNamedString(L"state",L"loading")!=L"loading")co_return;
        auto lifetime=shared_from_this();detailBusy=true;auto request=detailRequest;
        try
        {
            auto file=co_await ApplicationData::Current().LocalFolder().GetFileAsync(fixturePreview?L"detail-preview-content.json":L"detail.json");auto properties=co_await file.GetBasicPropertiesAsync();
            if(properties.Size()>4000000)throw hresult_invalid_argument();
            auto contents=co_await FileIO::ReadTextAsync(file);
            if(!stopped && request==detailRequest)
            {
                auto data=JsonObject::Parse(contents);
                if(fixturePreview)
                {
                    data.Insert(L"protocol",JsonValue::CreateNumberValue(1));data.Insert(L"session",JsonValue::CreateStringValue(session));
                    data.Insert(L"requestId",JsonValue::CreateStringValue(request));data.Insert(L"championId",JsonValue::CreateNumberValue(cards[selectedCard].hero));
                }
                if(data.GetNamedNumber(L"protocol",0)==1 && data.GetNamedString(L"session",L"")==session && data.GetNamedString(L"requestId",L"")==request
                    && selectedCard>=0 && data.GetNamedNumber(L"championId",0)==cards[selectedCard].hero)
                {
                    auto version=data.GetNamedString(L"revision",L"");
                    if(version!=detailRevision)
                    {
                        detailRevision=version;detailData=data;RenderDetails();
                        detailSource.Text(data.GetNamedString(L"mode",L"")+L" · "+data.GetNamedString(L"source",L""));
                    }
                }
            }
        }
        catch(...){ }
        if(!stopped && request==detailRequest && Now()-detailRequestedAt>50000 && detailData.GetNamedString(L"state",L"loading")==L"loading")
            detailSource.Text(L"主程序尚未返回详情，请确认已启动新版 League.exe 后重试。");
        detailBusy=false;
    }
