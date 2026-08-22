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
                ApplyCurrentLineFromExcel();
                await SaveAsync(Current).ConfigureAwait(false);
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
            Current = loaded ?? CreateDefault();
            SubscribeTopicHelper.Migrate(Current);
            ApplyCurrentLineFromExcel();
            await SaveAsync(Current).ConfigureAwait(false);
        }
        catch
        {
            Current = CreateDefault();
            ApplyCurrentLineFromExcel();
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

    void ApplyCurrentLineFromExcel()
    {
        LineConfigPaths.EnsureDefaultExcelFiles();
        if (string.IsNullOrWhiteSpace(Current.LineName))
        {
            Current.LineName = LineCatalog.LineNames[0];
        }

        LineConfigPaths.SyncCurrentLine(Current);
        if (Current.MqttPayload is not null)
        {
            MqttFieldMappingCatalog.NormalizeLegacyProfile(Current.MqttPayload);
        }

        LineMqttDefaults.MigrateLegacySettings(Current);
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.LineName = LineCatalog.LineNames[0];
        return settings;
    }
}
