// Included inside WidgetView. Each card shows the player's two summoner spells. Allies' are timed by hand: a click
// when the spell is used starts its cooldown from the main program (summoner haste and the mode's spell included),
// a second click cancels it. Enemies' only show which spells they have: Riot's rules forbid tracking enemy summoner
// cooldowns, manual timers included, so their icons take no clicks and carry no cooldown.
// Timers go to summoner-timers.json for the main program, which chimes when one is back and echoes the timers in
// the snapshot, so a restarted widget takes them back.
std::map<std::wstring, double> summonerTimers;
std::map<std::wstring, BitmapImage> summonerImages;
std::set<std::wstring> summonerImageLoads;
JsonObject summonerIconData, gameClock;
bool summonerTimersWritten = false, summonerTimersWriting = false, summonerSound = true, supportsSummonerChat = false;
// The ally card and spell a right-click sends; the send gate in TierChatView carries them into the request.
int summonerChatCard = -1, summonerChatSpell = -1;
hstring pendingSummonerTimers;
Button summonerSoundButton{nullptr};
static constexpr wchar_t const *SummonerSoundSetting = L"summonerReadySound";
static constexpr int64_t GameClockStaleMilliseconds = 15000;

StackPanel CreateCardSummoners(int index)
{
    StackPanel column;
    column.VerticalAlignment(VerticalAlignment::Center);
    column.Margin({6, 0, 0, 0});
    for (int slot = 0; slot < 2; ++slot)
    {
        auto &summoner = cards[index].summoners[slot];
        summoner.button.Width(24);
        summoner.button.Height(24);
        summoner.button.MinWidth(0);
        summoner.button.MinHeight(0);
        summoner.button.Padding({0});
        summoner.button.Margin({0, slot == 0 ? 0.0 : 2.0, 0, 0});
        summoner.button.BorderThickness({0});
        summoner.button.Background(MakeBrush(0x0b2530));
        Grid face;
        summoner.icon.Width(24);
        summoner.icon.Height(24);
        face.Children().Append(summoner.icon);
        summoner.shade.Background(MakeBrush(0x071e29));
        summoner.shade.Opacity(.55);
        summoner.shade.Visibility(Visibility::Collapsed);
        face.Children().Append(summoner.shade);
        summoner.countdown = MakeText(L"", 10, 0xffffff);
        summoner.countdown.FontWeight(Windows::UI::Text::FontWeights::Bold());
        summoner.countdown.HorizontalAlignment(HorizontalAlignment::Center);
        summoner.countdown.VerticalAlignment(VerticalAlignment::Center);
        face.Children().Append(summoner.countdown);
        summoner.button.Content(face);
        summoner.button.Click([this, index, slot](auto &&, auto &&) { ToggleSummonerTimer(index, slot); });
        summoner.button.RightTapped(
            [this, index, slot](auto &&, Windows::UI::Xaml::Input::RightTappedRoutedEventArgs const &e)
            {
                e.Handled(true);
                QueueSummonerChat(index, slot);
            });
        summoner.button.Visibility(Visibility::Collapsed);
        column.Children().Append(summoner.button);
    }
    return column;
}
static std::wstring SummonerTimerKey(Card const &card, int slot)
{
    return std::wstring(card.key) + L"#" + std::to_wstring(slot);
}
// Game seconds now from the snapshot's clock; negative when the data is too old to time with.
double SummonerGameTime()
{
    int64_t age = fixturePreview ? 0 : Now() - static_cast<int64_t>(gameClock.GetNamedNumber(L"clock", 0));
    if (gameClock.Size() == 0 || age < 0 || age > GameClockStaleMilliseconds)
        return -1;
    return gameClock.GetNamedNumber(L"gameTime", 0) + age / 1000.0;
}
// Seconds until the spell is back; 0 when it is not being timed or is already back.
double SummonerRemaining(Card const &card, int slot)
{
    auto const &summoner = card.summoners[slot];
    auto timer = summonerTimers.find(SummonerTimerKey(card, slot));
    double now = SummonerGameTime();
    if (!summoner.timed || timer == summonerTimers.end() || now < 0)
        return 0;
    return (std::max)(0.0, timer->second + summoner.cooldown - now);
}
void ToggleSummonerTimer(int index, int slot)
{
    auto &card = cards[index];
    auto const &summoner = card.summoners[slot];
    double now = SummonerGameTime();
    if (!summoner.timed || card.key.empty() || now < 0)
        return;
    auto key = SummonerTimerKey(card, slot);
    if (SummonerRemaining(card, slot) > 0)
        summonerTimers.erase(key);
    else
        summonerTimers[key] = now;
    RenderSummoner(card, slot);
    SaveSummonerTimers();
}
// Only a running or finished timer is sent; the main program checks again that the player is an ally.
void QueueSummonerChat(int index, int slot)
{
    auto &card = cards[index];
    if (!supportsSummonerChat || !card.summoners[slot].timed || chatPending)
        return;
    if (!summonerTimers.count(SummonerTimerKey(card, slot)))
    {
        chatStatus.Text(L"先左键点击技能图标开始计时，再右键发送。");
        return;
    }
    summonerChatCard = index;
    summonerChatSpell = slot;
    QueueChat(L"single", L"summoner");
}
void ApplySummonerData(JsonObject const &data)
{
    auto clock = data.TryLookup(L"gameClock");
    gameClock = clock && clock.ValueType() == JsonValueType::Object ? clock.GetObject() : JsonObject();
    summonerIconData = data.GetNamedObject(L"summonerIcons", JsonObject());
    for (auto const &entry : summonerIconData)
        LoadSummonerImage(std::wstring(entry.Key()),
                          entry.Value().ValueType() == JsonValueType::String ? entry.Value().GetString() : hstring());
}
void UpdateCardSummoners(Card &card, JsonObject const &row)
{
    auto list = row.GetNamedArray(L"summoners", JsonArray());
    for (int slot = 0; slot < 2; ++slot)
    {
        auto &summoner = card.summoners[slot];
        JsonObject value;
        for (auto const &entry : list)
            if (entry.ValueType() == JsonValueType::Object && entry.GetObject().GetNamedNumber(L"slot", -1) == slot)
                value = entry.GetObject();
        summoner.spell = value.GetNamedString(L"spell", L"");
        summoner.name = value.GetNamedString(L"name", L"");
        summoner.timed = value.GetNamedBoolean(L"timed", false) && !card.key.empty();
        summoner.cooldown = summoner.timed ? value.GetNamedNumber(L"cooldown", 0) : 0;
        summoner.timed = summoner.timed && summoner.cooldown > 0;
        // Until this widget changes a timer in the session, the main program's copy restores the last ones written.
        auto started = value.TryLookup(L"started");
        if (!summonerTimersWritten && summoner.timed && started && started.ValueType() == JsonValueType::Number)
            summonerTimers[SummonerTimerKey(card, slot)] = started.GetNumber();
        summoner.button.Visibility(summoner.spell.empty() ? Visibility::Collapsed : Visibility::Visible);
        summoner.button.IsHitTestVisible(summoner.timed);
        auto image = summonerImages.find(std::wstring(summoner.spell));
        summoner.icon.Source(image == summonerImages.end() ? nullptr : image->second);
        ToolTipService::SetToolTip(
            summoner.button,
            box_value(
                summoner.name +
                (summoner.timed
                     ? L"：用掉时左键开始计时，再点取消；右键发送计时到队伍聊天（只写技能名，可在 Tab 计分板点该英雄头像标明是谁）；冷却 " +
                           WaveClock(summoner.cooldown) + L"（已算召唤师技能急速）"
                     : L"")));
        RenderSummoner(card, slot);
    }
}
void RenderSummoner(Card &card, int slot)
{
    auto &summoner = card.summoners[slot];
    double remaining = SummonerRemaining(card, slot);
    bool counting = remaining > 0;
    summoner.shade.Visibility(counting ? Visibility::Visible : Visibility::Collapsed);
    // Without its icon yet, the button shows the spell's first character.
    if (counting)
        summoner.countdown.Text(to_hstring(static_cast<int>(std::ceil(remaining))));
    else if (summoner.icon.Source() == nullptr && !summoner.name.empty())
        summoner.countdown.Text(hstring(std::wstring(summoner.name).substr(0, 1)));
    else
        summoner.countdown.Text(L"");
}
void UpdateSummoners()
{
    if (stopped)
        return;
    for (auto &card : cards)
        for (int slot = 0; slot < 2; ++slot)
            if (card.summoners[slot].timed)
                RenderSummoner(card, slot);
}
fire_and_forget LoadSummonerImage(std::wstring spell, hstring data)
{
    if (data.empty() || summonerImages.count(spell) || !summonerImageLoads.insert(spell).second)
        co_return;
    auto lifetime = shared_from_this();
    try
    {
        auto buffer = Windows::Security::Cryptography::CryptographicBuffer::DecodeFromBase64String(data);
        if (buffer.Length() <= 100000)
        {
            InMemoryRandomAccessStream stream;
            co_await stream.WriteAsync(buffer);
            stream.Seek(0);
            BitmapImage bitmap;
            bitmap.DecodePixelWidth(24);
            co_await bitmap.SetSourceAsync(stream);
            if (!stopped)
            {
                summonerImages[spell] = bitmap;
                for (auto &card : cards)
                    for (int slot = 0; slot < 2; ++slot)
                        if (std::wstring(card.summoners[slot].spell) == spell)
                        {
                            card.summoners[slot].icon.Source(bitmap);
                            RenderSummoner(card, slot);
                        }
            }
        }
    }
    catch (hresult_error const &e)
    {
        // The button keeps the spell's first character; the next snapshot does not retry a broken icon.
        Trace(L"summoner-icon-error " + e.message());
    }
    summonerImageLoads.erase(spell);
}
void ClearSummoners()
{
    summonerTimers.clear();
    summonerTimersWritten = false;
    gameClock = JsonObject();
    for (auto &card : cards)
        for (auto &summoner : card.summoners)
        {
            summoner.spell = L"";
            summoner.name = L"";
            summoner.timed = false;
            summoner.cooldown = 0;
            summoner.icon.Source(nullptr);
            summoner.button.Visibility(Visibility::Collapsed);
        }
}
void SaveSummonerTimers()
{
    if (!widget || fixturePreview || stopped)
        return;
    summonerTimersWritten = true;
    JsonArray timers;
    for (auto const &card : cards)
        for (int slot = 0; slot < 2; ++slot)
        {
            auto timer = summonerTimers.find(SummonerTimerKey(card, slot));
            if (card.summoners[slot].timed && timer != summonerTimers.end())
            {
                JsonObject entry;
                entry.Insert(L"player", JsonValue::CreateStringValue(card.key));
                entry.Insert(L"slot", JsonValue::CreateNumberValue(slot));
                entry.Insert(L"time", JsonValue::CreateNumberValue(timer->second));
                timers.Append(entry);
            }
        }
    JsonObject file;
    file.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    file.Insert(L"session", JsonValue::CreateStringValue(session));
    file.Insert(L"timers", timers);
    pendingSummonerTimers = file.Stringify();
    WriteSummonerTimers();
}
fire_and_forget WriteSummonerTimers()
{
    if (summonerTimersWriting || stopped)
        co_return;
    auto lifetime = shared_from_this();
    summonerTimersWriting = true;
    try
    {
        while (!pendingSummonerTimers.empty() && !stopped)
        {
            auto value = pendingSummonerTimers;
            pendingSummonerTimers = L"";
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                L"summoner-timers.tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, value);
            co_await file.RenameAsync(L"summoner-timers.json", NameCollisionOption::ReplaceExisting);
        }
    }
    catch (hresult_error const &e)
    {
        // The card still counts down; only the main program's chime misses this change until the next one.
        Trace(L"summoner-timers-write-error " + e.message());
    }
    summonerTimersWriting = false;
}
void InitializeSummonerSound(StackPanel const &navigation)
{
    auto stored = ApplicationData::Current().LocalSettings().Values().TryLookup(SummonerSoundSetting);
    summonerSound = !stored || unbox_value_or<bool>(stored, true);
    summonerSoundButton = DetailButton(L"");
    summonerSoundButton.Margin({6, 0, 0, 0});
    summonerSoundButton.Click(
        [this](auto &&, auto &&)
        {
            summonerSound = !summonerSound;
            ApplicationData::Current().LocalSettings().Values().Insert(SummonerSoundSetting, box_value(summonerSound));
            RefreshSummonerSoundButton();
            SaveControlSettings();
        });
    ToolTipService::SetToolTip(summonerSoundButton,
                               box_value(L"由 League 主程序播放：我方卡片上手动开始的召唤师技能计时结束时响三声升调。"
                                         L"打开时不试播。敌方召唤师技能不计时。"));
    RefreshSummonerSoundButton();
    navigation.Children().Append(summonerSoundButton);
}
void RefreshSummonerSoundButton()
{
    summonerSoundButton.Content(MakeText(summonerSound ? L"召唤师提示音：开" : L"召唤师提示音：关", 13));
}
