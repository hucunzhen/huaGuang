namespace HuaGuang.Monitor.Services;

public sealed class MauiAppDataPaths : IAppDataPaths
{
    public string UserDataDirectory => Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
}
