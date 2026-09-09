using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace HuaGuang.Monitor.Services;

public static class AppVersionInfo
{
    const string VersionKey = "HuaGuang.Monitor.ReleaseVersion";
    const string RevisionKey = "HuaGuang.Monitor.ReleaseRevision";

    public static string Version =>
        ReadMetadata(VersionKey)
        ?? ReadInformationalVersion()
        ?? AppInfo.VersionString;

    public static string Revision =>
        ReadMetadata(RevisionKey)
        ?? FallbackRevision();

    public static string Display => $"{Version}（修订 {Revision}）";

    static string? ReadMetadata(string key)
    {
        foreach (var attribute in GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>() ?? [])
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    static string? ReadInformationalVersion()
    {
        var raw = GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }

    static string FallbackRevision()
    {
        var build = AppInfo.BuildString;
        return string.IsNullOrWhiteSpace(build) ? "1" : build;
    }

    static Assembly? GetEntryAssembly()
    {
        try
        {
            return Assembly.GetEntryAssembly();
        }
        catch
        {
            return null;
        }
    }
}
