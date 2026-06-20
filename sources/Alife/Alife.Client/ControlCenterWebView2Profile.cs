using System.IO;

namespace Alife;

public static class ControlCenterWebView2Profile
{
    public const string UserDataFolderEnvironmentVariable = "ALIFE_CONTROL_CENTER_WEBVIEW2_USER_DATA_FOLDER";

    public static string ResolveUserDataFolder(string runtimeFolder, string? userDataFolderOverride = null)
    {
        string userDataFolder = string.IsNullOrWhiteSpace(userDataFolderOverride)
            ? Path.Combine(runtimeFolder, "ControlCenter", "WebView2Data")
            : userDataFolderOverride;

        userDataFolder = Path.GetFullPath(userDataFolder);
        Directory.CreateDirectory(userDataFolder);
        return userDataFolder;
    }
}
