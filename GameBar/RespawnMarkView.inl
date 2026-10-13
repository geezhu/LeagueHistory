// Included inside WidgetView. Marked champions announce deaths and respawns from the 2999 respawn timers
// already in the snapshot; 2999 reports no vision, so nothing here tracks where a champion is.
// Chimes are played by the main program: Windows mutes this widget's audio while the game has focus.
Button markButton, markSoundButton;
TextBlock markAlert = MakeText(L"", 15, 0xffd27a);
bool markSound = true, marksWriting = false;
hstring pendingMarksWrite;
int64_t markSoundTest = 0;
static constexpr wchar_t const *MarkSoundSetting = L"respawnMarkSound";
static constexpr int64_t RevivedNoticeMilliseconds = 10000;
static constexpr int RespawnSoonSeconds = 5;

void InitializeMarks()
{
    markButton = DetailButton(L"☆ 标记复活");
    markButton.Margin({6, 0, 0, 0});
    markButton.Click(
        [this](auto &&, auto &&)
        {
            if (selectedCard >= 0)
                ToggleMark(selectedCard);
        });
    ToolTipService::SetToolTip(markButton, box_value(L"标记后，该英雄阵亡、即将复活和复活时在底部提醒；换局后清空。"));
    InitializeMarkSound();
    markAlert.TextWrapping(TextWrapping::Wrap);
    markAlert.Margin({0, 0, 0, 6});
    markAlert.Visibility(Visibility::Collapsed);
    // The detail navigation is the first child of the detail page; the buttons act on the hero shown there.
    auto navigation = detailRoot.Children().GetAt(0).as<StackPanel>();
    navigation.Children().Append(markButton);
    navigation.Children().Append(markSoundButton);
    // The footer stays visible on the roster, detail and wave pages.
    chatFooter.Children().InsertAt(0, markAlert);
}
void InitializeMarkSound()
{
    auto stored = ApplicationData::Current().LocalSettings().Values().TryLookup(MarkSoundSetting);
    markSound = !stored || unbox_value_or<bool>(stored, true);
    markSoundButton = DetailButton(L"");
    markSoundButton.Margin({6, 0, 0, 0});
    markSoundButton.Click(
        [this](auto &&, auto &&)
        {
            markSound = !markSound;
            ApplicationData::Current().LocalSettings().Values().Insert(MarkSoundSetting, box_value(markSound));
            // Turning sound on asks the main program for one sample chime.
            if (markSound)
                ++markSoundTest;
            RefreshMarkSoundButton();
            SaveMarks();
        });
    ToolTipService::SetToolTip(
        markSoundButton,
        box_value(L"由 League 主程序播放：标记英雄剩 5 秒复活时单响、复活时升调；打开时试播一声（对局中）。"));
    RefreshMarkSoundButton();
}
void RefreshMarkSoundButton()
{
    markSoundButton.Content(MakeText(markSound ? L"提示音：开" : L"提示音：关", 13));
}
void ToggleMark(int index)
{
    auto &card = cards[index];
    card.marked = !card.marked;
    card.wasDead = RespawnSeconds(card) > 0;
    card.revivedAt = 0;
    RenderMark(card);
    RefreshMarkButton();
    UpdateMarks();
    SaveMarks();
}
void RefreshMarkButton()
{
    bool marked = selectedCard >= 0 && cards[selectedCard].marked;
    markButton.Content(MakeText(marked ? L"★ 已标记复活" : L"☆ 标记复活", 13));
    markButton.Background(MakeBrush(marked ? 0x5a4a1e : 0x143542));
}
static void RenderMark(Card &card)
{
    card.button.Background(MakeBrush(card.marked ? 0x3a3418 : 0x112f3c));
}
void ClearMarks()
{
    for (auto &card : cards)
    {
        card.marked = false;
        card.wasDead = false;
        card.revivedAt = 0;
        RenderMark(card);
    }
    markAlert.Visibility(Visibility::Collapsed);
    SaveMarks();
}
// A new champion in a slot is a different player to watch, so its mark does not carry over.
void ForgetMarkIfReplaced(Card &card, int hero)
{
    if (card.marked && card.hero != hero)
    {
        card.marked = false;
        card.revivedAt = 0;
        RenderMark(card);
        SaveMarks();
    }
}
void UpdateMarks()
{
    std::wstring text;
    bool urgent = false;
    for (auto &card : cards)
    {
        if (!card.marked || card.hero <= 0)
            continue;
        int seconds = RespawnSeconds(card);
        bool dead = seconds > 0;
        if (!dead && card.wasDead)
            card.revivedAt = Now();
        card.wasDead = dead;
        std::wstring name(card.champion.Text());
        std::wstring line;
        if (dead && seconds <= RespawnSoonSeconds)
        {
            line = L"★ " + name + L" 即将复活 · " + std::to_wstring(seconds);
            urgent = true;
        }
        else if (dead)
            line = L"★ " + name + L" 阵亡 · " + std::to_wstring(seconds) + L" 秒后复活";
        else if (card.revivedAt > 0 && Now() - card.revivedAt < RevivedNoticeMilliseconds)
        {
            line = L"★ " + name + L" 已复活";
            urgent = true;
        }
        if (!line.empty())
            text += (text.empty() ? L"" : L"    ") + line;
    }
    markAlert.Text(text);
    markAlert.Foreground(MakeBrush(urgent ? 0xff8a7a : 0xffd27a));
    markAlert.Visibility(text.empty() ? Visibility::Collapsed : Visibility::Visible);
}
// Slots and the champions seen there, for the main program's chimes; the session ties marks to this match.
void SaveMarks()
{
    if (!widget || fixturePreview || stopped)
        return;
    JsonArray slots, heroes;
    for (int i = 0; i < 10; ++i)
    {
        if (cards[i].marked && cards[i].hero > 0)
        {
            slots.Append(JsonValue::CreateNumberValue(i));
            heroes.Append(JsonValue::CreateNumberValue(cards[i].hero));
        }
    }
    JsonObject marks;
    marks.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    marks.Insert(L"session", JsonValue::CreateStringValue(session));
    marks.Insert(L"slots", slots);
    marks.Insert(L"heroes", heroes);
    marks.Insert(L"sound", JsonValue::CreateBooleanValue(markSound));
    marks.Insert(L"test", JsonValue::CreateNumberValue(double(markSoundTest)));
    pendingMarksWrite = marks.Stringify();
    WriteMarks();
}
fire_and_forget WriteMarks()
{
    if (marksWriting || stopped)
        co_return;
    auto lifetime = shared_from_this();
    marksWriting = true;
    try
    {
        while (!pendingMarksWrite.empty() && !stopped)
        {
            auto value = pendingMarksWrite;
            pendingMarksWrite = L"";
            auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
                L"respawn-marks.tmp", CreationCollisionOption::ReplaceExisting);
            co_await FileIO::WriteTextAsync(file, value);
            co_await file.RenameAsync(L"respawn-marks.json", NameCollisionOption::ReplaceExisting);
        }
    }
    catch (hresult_error const &e)
    {
        // The text reminders still work; only the main program's chimes miss this change until the next one.
        Trace(L"respawn-marks-write-error " + e.message());
    }
    marksWriting = false;
}
