// Wave timing shared by the roster widget's wave page and the mini wave widget. The main program computes wave
// times from the rules and sends them in the snapshot's "waves" object; these functions only count down from it.
#pragma once
#include "WidgetCommon.h"

static constexpr int64_t WaveStaleMilliseconds = 5000;
// A wave spreads over about five seconds as its minions arrive one by one.
static constexpr double WaveArrivalSpread = 5;

static hstring WaveClock(double seconds)
{
    int total = static_cast<int>(seconds + .5);
    wchar_t text[16];
    swprintf_s(text, L"%d:%02d", total / 60, total % 60);
    return text;
}
static hstring WaveCountdown(double seconds)
{
    return seconds > 0 ? WaveClock(seconds) : hstring(L"0:00");
}
static bool WaveDataReady(JsonObject const &waves)
{
    return waves.GetNamedNumber(L"protocol", 0) == 1 && waves.GetNamedArray(L"spawns", JsonArray()).Size() > 0;
}
// Game seconds now, extrapolated from the snapshot; negative when the data is too old to count down. A frozen
// clock (fixture previews) reads the snapshot's game time as now.
static double WaveGameNow(JsonObject const &waves, bool frozen)
{
    int64_t age = frozen ? 0 : Now() - static_cast<int64_t>(waves.GetNamedNumber(L"clock", 0));
    if (age < 0 || age > WaveStaleMilliseconds)
        return -1;
    return waves.GetNamedNumber(L"gameTime", 0) + age / 1000.0;
}
// The first spawn still ahead; the snapshot lists a few so the countdown survives a missed poll.
static JsonObject NextSpawn(JsonObject const &waves, double now)
{
    for (auto const &value : waves.GetNamedArray(L"spawns", JsonArray()))
    {
        auto spawn = value.GetObject();
        if (spawn.GetNamedNumber(L"time", 0) > now)
            return spawn;
    }
    return nullptr;
}
// The drawn lane runs nexus to nexus, where the waves spawn. Converts bar fractions to the distance each side's
// waves have walked from their spawn.
struct LaneSpan
{
    double length = 0, spawn = 0, enemySpawn = 1;
    explicit LaneSpan(JsonObject const &progress)
        : length(progress.GetNamedNumber(L"length", 0)), spawn(progress.GetNamedNumber(L"spawn", 0)),
          enemySpawn(progress.GetNamedNumber(L"enemySpawn", 1))
    {
    }
    double AllyDistance(double at) const
    {
        return (at - spawn) * length;
    }
    double EnemyDistance(double at) const
    {
        return (enemySpawn - at) * length;
    }
};
static JsonObject LaneProgress(JsonObject const &lane)
{
    auto value = lane.TryLookup(L"progress");
    return value && value.ValueType() == JsonValueType::Object ? value.GetObject() : JsonObject();
}
// When a wave reaches a distance from its spawn if nothing stops it; negative without a speed.
static double WaveArrival(JsonObject const &wave, double distance)
{
    double speed = wave.GetNamedNumber(L"speed", 0);
    return speed <= 0
               ? -1
               : wave.GetNamedNumber(L"time", 0) + (std::max)(0.0, distance - wave.GetNamedNumber(L"bonus", 0)) / speed;
}
// The first wave of a timetable not yet past a distance; arrive is negative when none is left.
struct WaveArrivalTime
{
    double arrive = -1;
    bool cannon = false;
};
static WaveArrivalTime NextArrival(JsonArray const &timetable, double distance, double now)
{
    for (auto const &value : timetable)
    {
        auto wave = value.GetObject();
        double arrive = WaveArrival(wave, distance);
        if (arrive < 0 || arrive + WaveArrivalSpread <= now)
            continue;
        return {arrive, wave.GetNamedBoolean(L"cannon", false)};
    }
    return {};
}
// A walking wave's position: where it is at a game time, from its spawn up to the stop where it fights.
struct WavePath
{
    double time = 0, speed = 0, bonus = 0, reach = 0, origin = 0, direction = 1, length = 1;
    WavePath(JsonObject const &wave, LaneSpan const &span, double stop, bool ally)
        : time(wave.GetNamedNumber(L"time", 0)), speed(wave.GetNamedNumber(L"speed", 0)),
          bonus(wave.GetNamedNumber(L"bonus", 0)),
          // A wave stops at the chosen stop, where it would meet the other side's wave.
          reach((std::max)(0.0, ally ? span.AllyDistance(stop) : span.EnemyDistance(stop))),
          origin(ally ? span.spawn : span.enemySpawn), direction(ally ? 1 : -1), length(span.length)
    {
    }
    double At(double now) const
    {
        double walked = speed * (std::max)(0.0, now - time) + bonus;
        return origin + direction * (std::min)(walked, reach) / length;
    }
};
// Without a chosen stop, waves meet halfway between each side's outermost standing structure.
static double DefaultStop(JsonArray const &markers)
{
    double ally = 0, enemy = 1;
    for (auto const &value : markers)
    {
        auto marker = value.GetObject();
        if (!marker.GetNamedBoolean(L"alive", true))
            continue;
        double at = marker.GetNamedNumber(L"at", 0);
        if (marker.GetNamedBoolean(L"ally", false))
            ally = (std::max)(ally, at);
        else
            enemy = (std::min)(enemy, at);
    }
    return (ally + enemy) / 2;
}
// A main program older than the widget sends no kind; its labels still name the structure.
static hstring MarkerKind(JsonObject const &marker)
{
    auto kind = marker.GetNamedString(L"kind", L"");
    if (!kind.empty())
        return kind;
    auto label = marker.GetNamedString(L"label", L"");
    std::wstring_view name(label);
    if (name.find(L"大水晶") != std::wstring_view::npos)
        return L"base";
    if (name.find(L"门牙塔") != std::wstring_view::npos)
        return L"nexus";
    return name.find(L"水晶") != std::wstring_view::npos ? hstring(L"inhibitor") : hstring(L"turret");
}
// Whether a standing turret of the same side sits within 5% of the bar, too close for a second countdown label.
static bool HasCloseTurret(JsonArray const &markers, bool ally, double at)
{
    for (auto const &value : markers)
    {
        auto marker = value.GetObject();
        if (marker.GetNamedBoolean(L"ally", false) == ally && marker.GetNamedBoolean(L"alive", true) &&
            MarkerKind(marker) == L"turret" && std::abs(marker.GetNamedNumber(L"at", 0) - at) < 0.05)
            return true;
    }
    return false;
}
