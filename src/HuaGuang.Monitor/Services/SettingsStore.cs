using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    readonly string _filePath;

    public SettingsStore()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "settings.json");
        Current = CreateDefault();
    }

    public AppSettings Current { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                Current = CreateDefault();
                await SaveAsync(Current).ConfigureAwait(false);
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
            Current = loaded ?? CreateDefault();
            if (Current.AddressCatalogVersion < LineCatalog.Version || Current.Tags.Count == 0)
            {
                LineCatalog.Apply(Current, Current.LineName);
                await SaveAsync(Current).ConfigureAwait(false);
            }
        }
        catch
        {
            Current = CreateDefault();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = _filePath + ".tmp";
        await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
        File.Copy(temp, _filePath, overwrite: true);
        File.Delete(temp);
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, "先河热熔胶复合机");
        return settings;
    }
}
