// The voice recording and confirmed-text chat protocol with the main program, shared by the roster widget's voice
// row and the mini wave widget. Recognition runs in the main process; widgets only exchange small JSON files in
// LocalState. There is one recording and one chat request at a time across all widgets.
#pragma once
#include "WidgetCommon.h"

static constexpr size_t VoiceTextLimit = 180;
// The main program cancels a recording whose request is not refreshed for 3 seconds, and gives up after 55.
static constexpr int64_t VoiceTimeoutMilliseconds = 55000;
static constexpr int64_t ChatTimeoutMilliseconds = 32000;

static hstring ClipVoice(std::wstring text, size_t limit)
{
    if (text.size() > limit)
    {
        text.resize(limit);
        if (!text.empty() && text.back() >= 0xd800 && text.back() <= 0xdbff)
            text.pop_back();
    }
    return hstring(text);
}
// One chat line the main program accepts: at most 180 UTF-16 units, no control characters or broken surrogates, not
// blank and not a slash command. The result is trimmed.
static bool ConfirmedVoiceText(hstring const &value, hstring &result)
{
    std::wstring text(value);
    if (text.size() > VoiceTextLimit)
        return false;
    for (size_t i = 0; i < text.size(); ++i)
    {
        auto c = text[i];
        if (c < 32 || (c >= 0x7f && c <= 0x9f) || c == 0x2028 || c == 0x2029)
            return false;
        if (c >= 0xd800 && c <= 0xdbff)
        {
            if (++i >= text.size() || text[i] < 0xdc00 || text[i] > 0xdfff)
                return false;
        }
        else if (c >= 0xdc00 && c <= 0xdfff)
            return false;
    }
    auto first = text.find_first_not_of(L"                　");
    if (first == std::wstring::npos || text[first] == L'/')
        return false;
    auto last = text.find_last_not_of(L"                　");
    result = hstring(text.substr(first, last - first + 1));
    return true;
}
// A lowercase GUID without braces or dashes, the id format the main program expects for requests.
static hstring NewRequestId()
{
    GUID value{};
    check_hresult(CoCreateGuid(&value));
    std::wstring id;
    for (wchar_t c : to_hstring(guid(value)))
        if (c != L'{' && c != L'}' && c != L'-')
            id.push_back(c >= L'A' && c <= L'F' ? c + 32 : c);
    return hstring(id);
}
// action is start, stop or cancel; ack is the last status sequence applied. Rewriting it also renews the lease.
static JsonObject VoiceRequestJson(
    hstring const &id, hstring const &session, int64_t created, hstring const &action, int64_t ack)
{
    JsonObject data;
    data.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    data.Insert(L"id", JsonValue::CreateStringValue(id));
    data.Insert(L"session", JsonValue::CreateStringValue(session));
    data.Insert(L"createdAt", JsonValue::CreateNumberValue(double(created)));
    data.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(Now())));
    data.Insert(L"action", JsonValue::CreateStringValue(action));
    data.Insert(L"ack", JsonValue::CreateNumberValue(double(ack)));
    return data;
}
// A status belongs to this recording, is fresh and newer than the last one applied.
static bool VoiceStatusAccepted(JsonObject const &data, hstring const &id, hstring const &session, int64_t ack)
{
    auto age = Now() - data.GetNamedNumber(L"updatedAt", 0);
    auto sequence = data.GetNamedNumber(L"sequence", 0);
    return data.GetNamedNumber(L"protocol", 0) == 1 && data.GetNamedString(L"id", L"") == id &&
           data.GetNamedString(L"session", L"") == session && age >= 0 && age < 5000 && sequence > double(ack) &&
           sequence <= 1000000;
}
// A confirmed text for the current chat channel. dismissed tells the main program Game Bar has closed; it sends
// only once the game is also in the foreground.
static JsonObject VoiceChatRequestJson(
    hstring const &id, hstring const &session, hstring const &text, int64_t created, bool dismissed, bool cancelled)
{
    JsonObject request;
    request.Insert(L"protocol", JsonValue::CreateNumberValue(1));
    request.Insert(L"id", JsonValue::CreateStringValue(id));
    request.Insert(L"session", JsonValue::CreateStringValue(session));
    request.Insert(L"target", JsonValue::CreateStringValue(L"text"));
    request.Insert(L"kind", JsonValue::CreateStringValue(L"voice"));
    request.Insert(L"text", JsonValue::CreateStringValue(text));
    request.Insert(L"confirmed", JsonValue::CreateBooleanValue(true));
    request.Insert(L"slot", JsonValue::CreateNumberValue(-1));
    request.Insert(L"championId", JsonValue::CreateNumberValue(0));
    request.Insert(L"updatedAt", JsonValue::CreateNumberValue(double(created)));
    request.Insert(L"dismissed", JsonValue::CreateBooleanValue(dismissed));
    request.Insert(L"cancelled", JsonValue::CreateBooleanValue(cancelled));
    return request;
}
static bool ChatStatusMatches(JsonObject const &data, hstring const &id, hstring const &session)
{
    return data.GetNamedNumber(L"protocol", 0) == 1 && data.GetNamedString(L"requestId", L"") == id &&
           data.GetNamedString(L"session", L"") == session;
}
// Small bridge files: the main program replaces them atomically, so a missing or partial file is retried later.
static IAsyncOperation<JsonObject> ReadBridgeFileAsync(hstring const &name, uint64_t limit)
{
    auto file = co_await ApplicationData::Current().LocalFolder().GetFileAsync(name);
    auto properties = co_await file.GetBasicPropertiesAsync();
    if (properties.Size() > limit)
        throw hresult_invalid_argument();
    co_return JsonObject::Parse(co_await FileIO::ReadTextAsync(file));
}
static IAsyncAction WriteBridgeFileAsync(hstring const &temporary, hstring const &name, hstring const &value)
{
    auto file = co_await ApplicationData::Current().LocalFolder().CreateFileAsync(
        temporary, CreationCollisionOption::ReplaceExisting);
    co_await FileIO::WriteTextAsync(file, value);
    co_await file.RenameAsync(name, NameCollisionOption::ReplaceExisting);
}
// Removes a bridge file only while it still holds this widget's request; another widget may own a newer one.
static IAsyncAction DeleteBridgeFileIfOwnedAsync(hstring const &name, hstring const &id, uint64_t limit)
{
    try
    {
        auto data = co_await ReadBridgeFileAsync(name, limit);
        if (data.GetNamedString(L"id", L"") == id)
            co_await (co_await ApplicationData::Current().LocalFolder().GetFileAsync(name)).DeleteAsync();
    }
    catch (...)
    {
        // The main process may already have consumed and removed the file.
    }
}
