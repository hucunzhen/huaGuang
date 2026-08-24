using Microsoft.Maui.ApplicationModel;

namespace HuaGuang.Monitor.Services;

public static class AppVersionInfo
{
    public static string Version => AppInfo.VersionString;

    public static string Revision => AppInfo.BuildString;

    public static string Display => $"{Version}（修订 {Revision}）";
}
