namespace League.Services;

/// <summary>The version shown to users; Publish-Release.ps1 sets it with -p:Version.</summary>
public static class AppVersion
{
    // The SDK appends "+<commit>" to the informational version; users only need the release number.
    public static string Display => Application.ProductVersion.Split('+')[0];
}
