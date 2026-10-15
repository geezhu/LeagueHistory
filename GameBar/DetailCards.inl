// Included inside WidgetView. Detail cards for the record and live tabs: stat tiles, tag chips and game cards, with
// explanations as tooltips. Services/DetailCards.cs writes the sections.
static uint32_t RecordTone(hstring const &tone, uint32_t fallback = 0xe2eef3)
{
    if (tone == L"good" || tone == L"win")
        return 0x4fcf8f;
    if (tone == L"bad" || tone == L"loss")
        return 0xff8a7a;
    if (tone == L"warn")
        return 0xe0b25c;
    if (tone == L"info")
        return 0x65d6d6;
    if (tone == L"remake")
        return 0x7b8794;
    return fallback;
}
static void SetRecordTip(FrameworkElement const &element, hstring const &text)
{
    if (text.empty())
        return;
    auto tip = MakeText(text, 13);
    tip.TextWrapping(TextWrapping::Wrap);
    tip.MaxWidth(380);
    ToolTip tooltip;
    tooltip.Content(tip);
    ToolTipService::SetToolTip(element, tooltip);
}
// Equal columns keep tiles aligned whatever their text length; extra cells continue on the next row.
static Grid RecordGrid(std::vector<FrameworkElement> const &cells, int columns)
{
    Grid grid;
    for (int i = 0; i < columns; ++i)
        grid.ColumnDefinitions().Append(ColumnDefinition());
    for (size_t row = 0; row < (cells.size() + columns - 1) / columns; ++row)
    {
        RowDefinition definition;
        definition.Height({1, GridUnitType::Auto});
        grid.RowDefinitions().Append(definition);
    }
    for (size_t i = 0; i < cells.size(); ++i)
    {
        Grid::SetRow(cells[i], static_cast<int32_t>(i / columns));
        Grid::SetColumn(cells[i], static_cast<int32_t>(i % columns));
        grid.Children().Append(cells[i]);
    }
    return grid;
}
static Border RecordCard(UIElement const &content, uint32_t accent = 0)
{
    Border card;
    card.Background(MakeBrush(0x112f3c));
    card.CornerRadius({4, 4, 4, 4});
    card.Padding({10, 7, 10, 7});
    card.Margin({0, 0, 6, 6});
    if (accent)
    {
        card.BorderBrush(MakeBrush(accent));
        card.BorderThickness({3, 0, 0, 0});
    }
    card.Child(content);
    return card;
}
static Border RecordTile(JsonObject const &row, double size)
{
    StackPanel content;
    content.Children().Append(MakeText(row.GetNamedString(L"title", L""), 11, 0x91acbb));
    auto value = MakeText(row.GetNamedString(L"value", L""), size, RecordTone(row.GetNamedString(L"tone", L"")));
    value.Margin({0, 1, 0, 1});
    content.Children().Append(value);
    auto stats = row.GetNamedString(L"stats", L"");
    if (!stats.empty())
        content.Children().Append(MakeText(stats, 11, 0xa4bcc7));
    auto card = RecordCard(content);
    SetRecordTip(card, row.GetNamedString(L"description", L""));
    return card;
}
// Up to maxColumns per row; fewer tiles share the width instead of leaving empty columns.
static Grid RecordTiles(JsonArray const &rows, int maxColumns, double size)
{
    std::vector<FrameworkElement> cells;
    for (auto const &value : rows)
        cells.push_back(RecordTile(value.GetObject(), size));
    int columns = static_cast<int>(rows.Size()) < maxColumns ? static_cast<int>(rows.Size()) : maxColumns;
    return RecordGrid(cells, columns > 0 ? columns : 1);
}
static StackPanel RecordChips(JsonArray const &rows)
{
    StackPanel chips;
    chips.Orientation(Orientation::Horizontal);
    chips.Margin({0, 0, 0, 6});
    auto add = [&chips](hstring const &text, uint32_t color, hstring const &tip)
    {
        Border chip;
        chip.Background(MakeBrush(0x143542));
        chip.BorderBrush(MakeBrush(color));
        chip.BorderThickness({1, 1, 1, 1});
        chip.CornerRadius({12, 12, 12, 12});
        chip.Padding({12, 3, 12, 4});
        chip.Margin({0, 0, 6, 0});
        chip.Child(MakeText(text, 13, color));
        SetRecordTip(chip, tip);
        chips.Children().Append(chip);
    };
    for (auto const &value : rows)
    {
        auto tag = value.GetObject();
        add(tag.GetNamedString(L"title", L""),
            RecordTone(tag.GetNamedString(L"tone", L""), 0x65d6d6),
            tag.GetNamedString(L"description", L""));
    }
    if (rows.Size() == 0)
        add(L"无明显特征", 0x4fcf8f, L"最近的投降与信号次数都未达到任何标签的阈值。");
    return chips;
}
// Sections without a card layout keep the generic text rows. Smaller tiles ("signals") and icons follow the main
// cards of any layout.
bool RenderCardSection(JsonObject const &section)
{
    auto layout = section.GetNamedString(L"layout", L"");
    if (layout != L"tiles" && layout != L"chips" && layout != L"games")
        return false;
    RenderRecordHeading(section);
    auto rows = section.GetNamedArray(L"rows", JsonArray());
    if (layout == L"chips")
        detailBody.Children().Append(RecordChips(rows));
    else if (layout == L"games")
        detailBody.Children().Append(RecordGames(rows));
    else if (rows.Size() > 0)
        detailBody.Children().Append(RecordTiles(rows, 5, 18));
    RenderRecordForm(section.GetNamedArray(L"form", JsonArray()));
    auto signals = section.GetNamedArray(L"signals", JsonArray());
    if (signals.Size() > 0)
        detailBody.Children().Append(RecordTiles(signals, 6, 16));
    auto icons = section.GetNamedArray(L"icons", JsonArray());
    if (icons.Size() > 0)
        detailBody.Children().Append(DetailIcons(icons));
    return true;
}
void RenderRecordHeading(JsonObject const &section)
{
    StackPanel heading;
    heading.Orientation(Orientation::Horizontal);
    heading.Margin({0, 8, 0, 6});
    heading.Children().Append(MakeText(section.GetNamedString(L"title", L""), 15, 0xe0c688));
    auto tip = section.GetNamedString(L"tip", L"");
    if (!tip.empty())
    {
        auto info = MakeText(L"ⓘ", 13, 0x91acbb);
        info.Margin({6, 0, 0, 0});
        info.VerticalAlignment(VerticalAlignment::Center);
        heading.Children().Append(info);
        SetRecordTip(heading, tip);
    }
    detailBody.Children().Append(heading);
}
// One colored square per game, newest on the left: a season's form at a glance.
void RenderRecordForm(JsonArray const &form)
{
    if (form.Size() == 0)
        return;
    StackPanel strip;
    strip.Orientation(Orientation::Horizontal);
    strip.Margin({0, 0, 0, 4});
    auto label = MakeText(L"近 " + to_hstring(form.Size()) + L" 场", 11, 0x91acbb);
    label.VerticalAlignment(VerticalAlignment::Center);
    label.Margin({0, 0, 8, 0});
    strip.Children().Append(label);
    for (auto const &value : form)
    {
        auto game = value.GetObject();
        Border square;
        square.Width(14);
        square.Height(14);
        square.Margin({0, 0, 3, 0});
        square.CornerRadius({3, 3, 3, 3});
        square.Background(MakeBrush(RecordTone(game.GetNamedString(L"result", L""), 0x7b8794)));
        SetRecordTip(square, game.GetNamedString(L"description", L""));
        strip.Children().Append(square);
    }
    detailBody.Children().Append(strip);
}
Grid RecordGames(JsonArray const &rows)
{
    std::vector<FrameworkElement> cells;
    for (auto const &value : rows)
    {
        auto game = value.GetObject();
        uint32_t color = RecordTone(game.GetNamedString(L"tone", L""), 0x7b8794);
        Grid layout;
        ColumnDefinition avatar, details;
        avatar.Width({1, GridUnitType::Auto});
        details.Width({1, GridUnitType::Star});
        layout.ColumnDefinitions().Append(avatar);
        layout.ColumnDefinitions().Append(details);
        layout.Children().Append(RecordAvatar(game));
        StackPanel text;
        text.Margin({8, 0, 0, 0});
        Grid::SetColumn(text, 1);
        StackPanel header;
        header.Orientation(Orientation::Horizontal);
        auto result = MakeText(game.GetNamedString(L"result", L""), 12, color);
        result.Margin({0, 0, 6, 0});
        header.Children().Append(result);
        header.Children().Append(MakeText(game.GetNamedString(L"title", L""), 13));
        auto badge = game.GetNamedString(L"badge", L"");
        if (!badge.empty())
        {
            auto mark = MakeText(badge, 11, 0xe0b25c);
            mark.Margin({6, 1, 0, 0});
            header.Children().Append(mark);
        }
        text.Children().Append(header);
        text.Children().Append(MakeText(game.GetNamedString(L"value", L""), 15, 0xe2eef3));
        text.Children().Append(MakeText(game.GetNamedString(L"stats", L""), 11, 0x91acbb));
        layout.Children().Append(text);
        auto card = RecordCard(layout, color);
        SetRecordTip(card, game.GetNamedString(L"description", L""));
        cells.push_back(card);
    }
    return RecordGrid(cells, 3);
}
// The champion icon from the player's history; the name's first character when the history has none.
Border RecordAvatar(JsonObject const &game)
{
    Border frame;
    frame.Width(32);
    frame.Height(32);
    frame.CornerRadius({4, 4, 4, 4});
    frame.Background(MakeBrush(0x1d4c57));
    frame.VerticalAlignment(VerticalAlignment::Center);
    auto data = game.GetNamedString(L"image", L"");
    if (!data.empty())
    {
        Image image;
        image.Stretch(Stretch::UniformToFill);
        DecodeDetailImage(image, data, detailRender, 32);
        frame.Child(image);
        return frame;
    }
    auto name = game.GetNamedString(L"title", L"?");
    auto initial = MakeText(hstring(std::wstring_view(name).substr(0, 1)), 14, 0xe2eef3);
    initial.HorizontalAlignment(HorizontalAlignment::Center);
    initial.VerticalAlignment(VerticalAlignment::Center);
    frame.Child(initial);
    return frame;
}
