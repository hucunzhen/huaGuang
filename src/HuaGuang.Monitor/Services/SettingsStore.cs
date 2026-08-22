using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class SettingsStore
{
    public const int CurrentSettingsMigrationVersion = 1;

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
        LineConfigPaths.EnsureDefaultExcelFiles();

        try
        {
            if (!File.Exists(_filePath))
            {
                Current = CreateDefault();
                ApplyCurrentLineFromExcel();
                RunOneTimeMigrations();
                await SaveAsync(Current).ConfigureAwait(false);
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
            Current = loaded ?? CreateDefault();
            SubscribeTopicHelper.Migrate(Current);

            var modified = RunOneTimeMigrations();
            if (NeedsLineDataRefresh())
            {
                ApplyCurrentLineFromExcel();
                modified = true;
            }
            else
            {
                NormalizeMqttPayloadProfile();
            }

            if (modified)
            {
                await SaveAsync(Current).ConfigureAwait(false);
            }
        }
        catch
        {
            Current = CreateDefault();
            ApplyCurrentLineFromExcel();
            RunOneTimeMigrations();
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

    static bool NeedsLineDataRefresh(AppSettings settings) =>
        settings.Tags.Count == 0 ||
        settings.AddressCatalogVersion < LineCatalog.Version;

    bool NeedsLineDataRefresh() => NeedsLineDataRefresh(Current);

    bool RunOneTimeMigrations()
    {
        if (Current.SettingsMigrationVersion >= CurrentSettingsMigrationVersion)
        {
            return false;
        }

        Current.SettingsMigrationVersion = CurrentSettingsMigrationVersion;
        return true;
    }

    void ApplyCurrentLineFromExcel()
    {
        if (string.IsNullOrWhiteSpace(Current.LineName))
        {
            Current.LineName = LineCatalog.LineNames[0];
        }

        LineConfigPaths.SyncCurrentLine(Current);
        Current.AddressCatalogVersion = LineCatalog.Version;
        NormalizeMqttPayloadProfile();
    }

    void NormalizeMqttPayloadProfile()
    {
        if (Current.MqttPayload is not null)
        {
            MqttFieldMappingCatalog.NormalizeLegacyProfile(Current.MqttPayload);
        }
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.LineName = LineCatalog.LineNames[0];
        return settings;
    }
}
