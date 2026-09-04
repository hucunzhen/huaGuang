using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 运行时配置缓存；持久化只写当前产线对应的 Excel（与 config/lines 同源结构）。
/// </summary>
public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger)
    {
        _logger = logger;
        Current = CreateDefault();
    }

    public AppSettings Current { get; private set; }

    public async Task LoadAsync()
    {
        LineConfigPaths.EnsureAllLineExcels();
        MigrateLegacySingleConfigFile();
        MigrateLegacyJsonIfPresent();

        var lineName = LineConfigPaths.ReadActiveLineName();
        var templatePath = LineConfigPaths.ResolveShippedLineExcelPath(lineName);
        Current = LineExcelConfigService.LoadLineExcel(
            lineName,
            LineConfigPaths.GetLineExcelPath(lineName),
            templatePath);
        SubscribeTopicHelper.Migrate(Current);
        LineMqttDefaults.MigrateLegacySettings(Current);
        RunStatusFormatting.MigrateTags(Current.Tags);
        NormalizeMqttPayloadProfile();

        var catalogTags = LineCatalog.Resolve(Current.LineName).Tags;
        var tagCountBeforeMerge = Current.Tags.Count;
        LineExcelConfigService.MergeMissingRequiredPlcTags(Current, catalogTags);
        MqttFieldMappingCatalog.ApplyDefaults(Current.Tags, Current.LineName);
        if (Current.Tags.Count > tagCountBeforeMerge)
        {
            _logger.LogInformation(
                "已合并缺失 catalog 点位 line={LineName} added={AddedCount}",
                Current.LineName,
                Current.Tags.Count - tagCountBeforeMerge);
            await SaveAsync(Current).ConfigureAwait(false);
        }
        else if (Current.AddressCatalogVersion < LineCatalog.Version)
        {
            Current.AddressCatalogVersion = LineCatalog.Version;
            await SaveAsync(Current).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "配置已加载 line={LineName} mode={Mode} excel={ExcelPath} plc={Plc} mqtt={Mqtt}",
            Current.LineName,
            Current.OperationMode,
            LineConfigPaths.GetLineExcelPath(Current.LineName),
            LogFormatting.DescribePlc(Current.Plc),
            LogFormatting.DescribeMqtt(Current.Mqtt, Current.LineName));
    }

    public Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        SubscribeTopicHelper.Migrate(Current);
        NormalizeMqttPayloadProfile();
        Current.AddressCatalogVersion = LineCatalog.Version;
        var excelPath = LineConfigPaths.GetLineExcelPath(Current.LineName);
        LineConfigPaths.SaveLine(Current);
        _logger.LogInformation(
            "配置已保存 line={LineName} excel={ExcelPath}",
            Current.LineName,
            excelPath);
        return Task.CompletedTask;
    }

    static void NormalizeMqttPayloadProfile(AppSettings settings)
    {
        if (settings.MqttPayload is not null)
        {
            MqttFieldMappingCatalog.NormalizeLegacyProfile(settings.MqttPayload);
        }
    }

    void NormalizeMqttPayloadProfile() => NormalizeMqttPayloadProfile(Current);

    void MigrateLegacySingleConfigFile()
    {
        var legacyPath = Path.Combine(LineConfigPaths.LinesDirectory, "产线配置.xlsx");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var settings = LineExcelConfigService.LoadLineExcelFromFile(legacyPath, templateFilePath: null);
            if (string.IsNullOrWhiteSpace(settings.LineName))
            {
                settings.LineName = LineCatalog.LineNames[0];
            }

            LineConfigPaths.WriteActiveLineName(settings.LineName);
            LineExcelConfigService.Export(settings, LineConfigPaths.GetLineExcelPath(settings.LineName));
            var backup = legacyPath + ".migrated";
            File.Copy(legacyPath, backup, overwrite: true);
            File.Delete(legacyPath);
            _logger.LogInformation(
                "已迁移旧版单文件配置 line={LineName} backup={BackupPath}",
                settings.LineName,
                backup);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "迁移旧版单文件配置失败 path={LegacyPath}", legacyPath);
        }
    }

    void MigrateLegacyJsonIfPresent()
    {
        var jsonPath = AppPaths.SettingsFilePath;
        if (!File.Exists(jsonPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var legacy = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (legacy is not null)
            {
                if (string.IsNullOrWhiteSpace(legacy.LineName))
                {
                    legacy.LineName = LineCatalog.LineNames[0];
                }

                LineMqttDefaults.MigrateLegacySettings(legacy);
                LineConfigPaths.WriteActiveLineName(legacy.LineName);
                LineExcelConfigService.Export(legacy, LineConfigPaths.GetLineExcelPath(legacy.LineName));
            }

            var backupPath = jsonPath + ".migrated";
            File.Copy(jsonPath, backupPath, overwrite: true);
            File.Delete(jsonPath);
            _logger.LogInformation(
                "已迁移旧版 settings.json line={LineName} backup={BackupPath}",
                legacy?.LineName ?? "(unknown)",
                backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "迁移旧版 settings.json 失败 path={JsonPath}", jsonPath);
        }
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.LineName = LineCatalog.LineNames[0];
        return settings;
    }
}
