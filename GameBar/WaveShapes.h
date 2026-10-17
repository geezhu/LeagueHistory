// Lane bar drawing shared by the roster widget's wave page and the mini wave widget.
#pragma once
#include "WaveModel.h"

static void SetFraction(Grid const &layer, double at)
{
    at = std::clamp(at, 0.0, 1.0);
    auto columns = layer.ColumnDefinitions();
    columns.GetAt(0).Width({at, GridUnitType::Star});
    columns.GetAt(2).Width({1 - at, GridUnitType::Star});
}
// A child placed at a fraction of the bar with star columns, so it follows resizes without a Canvas.
static Grid AtFraction(UIElement const &child, double at)
{
    Grid layer;
    ColumnDefinition left, middle, right;
    middle.Width({1, GridUnitType::Auto});
    layer.ColumnDefinitions().Append(left);
    layer.ColumnDefinitions().Append(middle);
    layer.ColumnDefinitions().Append(right);
    SetFraction(layer, at);
    Grid::SetColumn(child.as<FrameworkElement>(), 1);
    layer.Children().Append(child);
    return layer;
}
// A walking wave's dot on a bar; moved every frame from its path.
struct WaveDot
{
    Grid layer{nullptr};
    WavePath path;
};
static void MoveWaveDots(std::vector<WaveDot> const &dots, double now)
{
    if (now < 0)
        return;
    for (auto const &dot : dots)
        SetFraction(dot.layer, dot.path.At(now));
}
static uint32_t Lighten(uint32_t color)
{
    auto mix = [color](int shift) { return (((color >> shift) & 0xff) * 3 + 0xff * 2) / 5 << shift; };
    return mix(16) | mix(8) | mix(0);
}
// Draws the points as given, or scaled to the size when stretched.
template <typename Points>
static Windows::UI::Xaml::Shapes::Polygon CreatePolygon(
    Points const &points, double width, double height, uint32_t color, bool stretch = true)
{
    Windows::UI::Xaml::Shapes::Polygon shape;
    PointCollection collection;
    for (auto const &point : points)
        collection.Append(point);
    shape.Points(collection);
    shape.Fill(MakeBrush(color));
    if (stretch)
    {
        shape.Stretch(Stretch::Fill);
        shape.Width(width);
        shape.Height(height);
    }
    return shape;
}
// Two faces, the left one lighter, so the crystal reads as cut stone rather than a flat shape.
static Grid CreateCrystal(std::initializer_list<Windows::Foundation::Point> outline,
                          std::initializer_list<Windows::Foundation::Point> face,
                          double width,
                          double height,
                          uint32_t color)
{
    Grid crystal;
    crystal.Width(width);
    crystal.Height(height);
    crystal.Children().Append(CreatePolygon(outline, width, height, color, false));
    crystal.Children().Append(CreatePolygon(face, width, height, Lighten(color), false));
    return crystal;
}
// Minimap-like silhouettes: a turret, the two nexus turrets, the inhibitor crystal and the larger nexus crystal.
static FrameworkElement CreateStructureIcon(hstring const &kind, uint32_t color)
{
    using Windows::Foundation::Point;
    static const std::array<Point, 18> tower{{{1, 18},
                                              {11, 18},
                                              {9, 13},
                                              {9, 7},
                                              {11, 7},
                                              {11, 1},
                                              {9, 1},
                                              {9, 3},
                                              {7, 3},
                                              {7, 1},
                                              {5, 1},
                                              {5, 3},
                                              {3, 3},
                                              {3, 1},
                                              {1, 1},
                                              {1, 7},
                                              {3, 7},
                                              {3, 13}}};
    if (kind == L"inhibitor")
        return CreateCrystal({{6, 0}, {12, 7}, {6, 18}, {0, 7}}, {{6, 0}, {6, 18}, {0, 7}}, 12, 18, color);
    if (kind == L"base")
        return CreateCrystal(
            {{9, 0}, {17, 5}, {17, 15}, {9, 20}, {1, 15}, {1, 5}}, {{9, 0}, {9, 20}, {1, 15}, {1, 5}}, 18, 20, color);
    if (kind == L"nexus")
    {
        StackPanel pair;
        pair.Orientation(Orientation::Horizontal);
        pair.Children().Append(CreatePolygon(tower, 7, 13, color));
        auto second = CreatePolygon(tower, 7, 13, color);
        second.Margin({1, 0, 0, 0});
        pair.Children().Append(second);
        return pair;
    }
    return CreatePolygon(tower, 11, 16, color);
}
