using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 运行时配置缓存；持久化只写当前产线对应的 Excel（与 config/lines 同源结构）。
/// </summary>
public sealed class SettingsStore
{
    readonly ILogger<SettingsStore> _logger;

    public SettingsStore(ILogger<SettingsStore> logger)
    {
        _logger = logger;
        Current = CreateDefault();
    }

    public AppSettings Current { get; private set; }

    /// <summary>配置变更序号；Save/Load 成功后递增，供 UI 判断是否需要重建监控卡片。</summary>
    public int Revision { get; private set; }

    ConfigFingerprint _loadedFingerprint;

    readonly record struct ConfigFingerprint(
        string ActiveLineName,
        long ActiveLineFileUtcTicks,
        long LineExcelUtcTicks,
        int CatalogVersion);

    public async Task<bool> LoadAsyncIfChanged()
    {
        var pending = CaptureConfigFingerprint();
        if (pending == _loadedFingerprint && Current.Tags.Count > 0)
        {
            return false;
        }

        await LoadAsync().ConfigureAwait(false);
        return true;
    }

    public Task LoadAsync()
    {
        LineConfigPaths.EnsureAllLineExcels();

        var lineName = LineConfigPaths.ReadActiveLineName();
        Current = LineExcelConfigService.LoadLineExcel(
            lineName,
            LineConfigPaths.GetLineExcelPath(lineName),
            templateFilePath: null);

        _logger.LogInformation(
            "配置已加载 line={LineName} mode={Mode} excel={ExcelPath} plc={Plc} mqtt={Mqtt}",
            Current.LineName,
            Current.OperationMode,
            LineConfigPaths.GetLineExcelPath(Current.LineName),
            LogFormatting.DescribePlc(Current.Plc),
            LogFormatting.DescribeMqtt(Current.Mqtt, Current.LineName));

        _loadedFingerprint = CaptureConfigFingerprint();
        Revision++;
        return Task.CompletedTask;
    }

    public Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        var excelPath = LineConfigPaths.GetLineExcelPath(Current.LineName);
        LineConfigPaths.SaveLine(Current);
        _loadedFingerprint = CaptureConfigFingerprint();
        Revision++;
        _logger.LogInformation(
            "配置已保存 line={LineName} excel={ExcelPath}",
            Current.LineName,
            excelPath);
        return Task.CompletedTask;
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.LineName = LineCatalog.LineNames[0];
        return settings;
    }

    ConfigFingerprint CaptureConfigFingerprint()
    {
        var lineName = LineConfigPaths.ReadActiveLineName();
        var excelPath = LineConfigPaths.GetLineExcelPath(lineName);
        return new ConfigFingerprint(
            lineName,
            File.Exists(LineConfigPaths.ActiveLineFilePath)
                ? File.GetLastWriteTimeUtc(LineConfigPaths.ActiveLineFilePath).Ticks
                : 0,
            File.Exists(excelPath)
                ? File.GetLastWriteTimeUtc(excelPath).Ticks
                : 0,
            LineCatalog.Version);
    }
}
