// Shared by every widget view in this single translation unit: headers, namespaces, brushes, text and logs.
#pragma once
#include <windows.h>
#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.ApplicationModel.Activation.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Graphics.Imaging.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Security.Cryptography.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.Storage.FileProperties.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Documents.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <winrt/Microsoft.Gaming.XboxGameBar.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <memory>
#include <fstream>
#include <vector>
#include <functional>
#include <map>
#include <set>
#include <cwctype>
#include <cmath>

using namespace winrt;
using namespace Windows::ApplicationModel::Activation;
using namespace Windows::Foundation;
using namespace Windows::Data::Json;
using namespace Windows::Storage;
using namespace Windows::Storage::Streams;
using namespace Windows::UI;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;
using namespace Windows::UI::Xaml::Media::Imaging;
using namespace Microsoft::Gaming::XboxGameBar;

static SolidColorBrush MakeBrush(uint32_t rgb)
{
    return SolidColorBrush(Color{255, uint8_t(rgb >> 16), uint8_t(rgb >> 8), uint8_t(rgb)});
}
static TextBlock MakeText(hstring const &value, double size = 14, uint32_t color = 0xe2eef3)
{
    TextBlock text;
    text.Text(value);
    text.FontSize(size);
    text.Foreground(MakeBrush(color));
    text.TextTrimming(TextTrimming::CharacterEllipsis);
    return text;
}
static int64_t Now()
{
    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch())
        .count();
}
// Appends one line; past 64 KB the file becomes <name>.previous.txt so the latest history survives rotation.
// The desktop app collects both files into its diagnostic report.
static void AppendBounded(wchar_t const *name, wchar_t const *previous, hstring const &message) noexcept
{
    try
    {
        std::wstring folder{ApplicationData::Current().LocalFolder().Path()};
        std::wstring path = folder + L"\\" + name;
        WIN32_FILE_ATTRIBUTE_DATA info{};
        if (GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &info) &&
            (info.nFileSizeHigh != 0 || info.nFileSizeLow > 65536))
        {
            MoveFileExW(path.c_str(), (folder + L"\\" + previous).c_str(), MOVEFILE_REPLACE_EXISTING);
        }
        std::ofstream log(path.c_str(), std::ios::app);
        log << Now() << " " << to_string(message) << "\n";
    }
    catch (...)
    {
        // Logging must remain best effort when the app-local directory is unavailable.
    }
}
static void LogError(hstring const &message) noexcept
{
    AppendBounded(L"component-error.txt", L"component-error.previous.txt", message);
}
static void Trace(hstring const &message) noexcept
{
    // Diagnostics must not interrupt the Game Bar window when its directory is unavailable.
    AppendBounded(L"component-diagnostics.txt", L"component-diagnostics.previous.txt", message);
}
static hstring PackageVersion() noexcept
{
    try
    {
        auto version = Windows::ApplicationModel::Package::Current().Id().Version();
        return to_hstring(version.Major) + L"." + to_hstring(version.Minor) + L"." + to_hstring(version.Build) + L"." +
               to_hstring(version.Revision);
    }
    catch (...)
    {
        // An unpackaged preview run has no package identity.
        return L"unpackaged";
    }
}
// The main program's snapshot, or a preview fixture, from the package's LocalState.
static IAsyncOperation<JsonObject> ReadLocalSnapshotAsync(hstring const &name)
{
    auto folder = ApplicationData::Current().LocalFolder();
    auto file = co_await folder.GetFileAsync(name);
    auto properties = co_await file.GetBasicPropertiesAsync();
    if (properties.Size() > 2000000)
        throw hresult_invalid_argument();
    auto contents = co_await FileIO::ReadTextAsync(file);
    co_return JsonObject::Parse(contents);
}
