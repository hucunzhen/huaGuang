using ClosedXML.Excel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 每条产线一个 Excel，包含该产线的全部配置（PLC、MQTT、报文格式、字段映射、点表）。
/// </summary>
public static class LineExcelConfigService
{
    public const int FormatVersion = 3;

    public const string ConfigSheetName = "配置";
    public const string MqttSheetName = "MQTT报文";
    public const string FieldMappingSheetName = MqttFieldMappingImporter.FieldMappingSheetName;
    public const string TagsSheetName = "点表";
    public const string DisplayCategorySheetName = "显示分组说明";

    static readonly string[] TagHeaders =
    [
        "名称", "来源", "地址", "数据类型", "单位", "字节序", "启用", "手动默认值", "精度", "倍率", "偏移", "显示分组", "扫码输入"
    ];

    static readonly (string Key, string Label, string Hint)[] MqttPayloadRows =
    [
        ("PayloadFormat", "报文格式", "json"),
        ("TagsPath", "点位容器路径", "如 properties（推荐）/ tags / data.tags；留空=平铺在根级"),
        ("DeviceIdPath", "设备ID字段", "留空则不写入；设备编号仍用于主题 {deviceId}"),
        ("TimestampPath", "时间戳字段", "留空则不写入；如 timestamp"),
        ("TimestampFormat", "时间戳格式", "iso8601 / unix_ms / unix_s（仅写入时有效）"),
        ("QualityPath", "质量字段", "留空则不写入；如 quality"),
        ("PlcHostPath", "PLC地址字段", "留空则不写入；如 plcHost"),
        ("SimulatorPath", "模拟模式字段", "留空则不写入；如 simulator"),
        ("UseTagNameWhenFieldEmpty", "未映射时用点位名称", "是/否"),
    ];

    static readonly (string Key, string Label)[] ConfigRows =
    [
        ("ExcelFormatVersion", "配置版本"),
        ("LineConfigRevision", "产线配置版本"),
        ("LineName", "产线名称"),
        ("DeviceId", "设备编号"),
        ("PlcModel", "PLC型号"),
        ("PlcHost", "PLC_IP"),
        ("PlcPort", "PLC端口"),
        ("PlcStation", "PLC站号"),
        ("PlcTimeoutMs", "PLC超时毫秒"),
        ("ScanIntervalMs", "扫描周期毫秒"),
        ("TemperaturePublishThresholdC", "温度发布阈值"),
        ("TemperaturePrecision", "精度"),
        ("UseSimulator", "使用模拟数据"),
        ("MqttHost", "MQTT_Broker"),
        ("MqttPort", "MQTT端口"),
        ("MqttClientId", "MQTT_ClientId"),
        ("MqttUsername", "MQTT_用户名"),
        ("MqttPassword", "MQTT_密码"),
        ("MqttUseTls", "MQTT_TLS"),
        ("MqttQos", "MQTT_QoS"),
        ("MqttTopic", "MQTT发布主题"),
        ("SubscribeTopics", "订阅主题"),
        ("OperationMode", "运行模式"),
        ("StartWithWindows", "开机自动启动"),
        ("AutoStartAcquisition", "启动后自动运行"),
        ("EnableHistoryRecording", "记录历史数据"),
        ("HistoryRetentionDays", "历史保留天数"),
    ];

    public static void Export(AppSettings settings, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        using var workbook = new XLWorkbook();
        WriteConfigSheet(workbook, settings);
        WriteMqttPayloadSheet(workbook, settings.MqttPayload);
        WriteFieldMappingSheet(workbook, settings.Tags);
        WriteTagsSheet(workbook, settings.Tags);
        WriteDisplayCategorySheet(workbook);
        workbook.SaveAs(filePath);
    }

    public static void Apply(AppSettings settings, string filePath, string? expectedLineName = null, string? templateFilePath = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"找不到产线配置文件：{filePath}");
        }

        using var workbook = new XLWorkbook(filePath);
        ApplyWorkbook(settings, workbook, expectedLineName, templateFilePath);
    }

    /// <summary>从 Excel 加载；点表以 Excel「点表」工作表为准，不合并代码内置点位。</summary>
    public static AppSettings LoadLineExcel(string lineName, string filePath, string? templateFilePath = null)
    {
        EnsureLineFile(filePath, lineName, templateFilePath);
        return LoadLineExcelFromFile(filePath, templateFilePath, lineName);
    }

    public static AppSettings LoadLineExcelFromFile(string filePath, string? templateFilePath, string? expectedLineName = null)
    {
        var settings = new AppSettings();
        Apply(settings, filePath, expectedLineName, templateFilePath);
        if (string.IsNullOrWhiteSpace(settings.LineName))
        {
            settings.LineName = expectedLineName ?? LineCatalog.LineNames[0];
        }

        return settings;
    }

    public static AppSettings SwitchLine(
        string lineName,
        string filePath,
        string? templateFilePath,
        AppSettings? preserveFrom = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var useTemplate = !File.Exists(filePath);
        if (File.Exists(filePath))
        {
            using var workbook = new XLWorkbook(filePath);
            useTemplate = !ConfigSheetMatchesLine(workbook, lineName);
        }

        if (useTemplate)
        {
            if (templateFilePath is null || !File.Exists(templateFilePath))
            {
                throw new FileNotFoundException(
                    $"找不到产线「{lineName}」的原始 Excel，请确认安装目录 lines 下存在 {lineName}.xlsx（来自 config/lines）。");
            }

            File.Copy(templateFilePath, filePath, overwrite: true);
        }

        var settings = LoadLineExcel(lineName, filePath, templateFilePath);
        CopyPreserveFrom(preserveFrom, settings);
        Export(settings, filePath);
        return settings;
    }

    [Obsolete("Use SwitchLine or LoadLineExcel.")]
    public static AppSettings LoadLineSettings(string lineName, string filePath, AppSettings? preserveFrom = null) =>
        SwitchLine(lineName, filePath, null, preserveFrom);

    public static AppSettings LoadConfig(string configFilePath) =>
        LoadLineExcelFromFile(configFilePath, templateFilePath: null);

    public static void ApplyWorkbook(
        AppSettings settings,
        XLWorkbook workbook,
        string? expectedLineName = null,
        string? templateFilePath = null)
    {
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out _))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{ConfigSheetName}」。");
        }

        ApplyConfigSheet(settings, workbook, expectedLineName, templateFilePath);
        ApplyMqttPayloadSheet(settings, workbook);
        settings.Tags = ReadTagsSheet(workbook);
        ApplyFieldMappings(settings, workbook);
        settings.AddressCatalogVersion = LineCatalog.Version;
    }

    public static void EnsureLineFile(string filePath, string lineName, string? templateFilePath = null)
    {
        EnsureLineFileExists(filePath, lineName, templateFilePath);
        if (!File.Exists(filePath))
        {
            return;
        }

        if (NeedsFormatUpgrade(filePath))
        {
            var settings = LoadLineExcelFromFile(filePath, templateFilePath, lineName);
            using var workbook = new XLWorkbook(filePath);
            TryApplyExistingSheets(settings, workbook);
            Export(settings, filePath);
            return;
        }

        ApplyLineFileMaintenance(filePath);
    }

    /// <summary>
    /// 产线 Excel 已存在时只做局部维护（运行状态/精度键名、版本号），不重写点表。
    /// </summary>
    public static void ApplyLineFileMaintenance(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        PatchRunStatusAndPrecision(filePath);
        if (NeedsRevisionUpgrade(filePath))
        {
            UpdateLineConfigRevisionInPlace(filePath, LineCatalog.Version);
        }
    }

    static void EnsureLineFileExists(string filePath, string lineName, string? templateFilePath)
    {
        _ = lineName;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        if (File.Exists(filePath))
        {
            return;
        }

        if (templateFilePath is not null && File.Exists(templateFilePath))
        {
            File.Copy(templateFilePath, filePath);
        }
    }

    static void UpdateLineConfigRevisionInPlace(string filePath, int revision)
    {
        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            return;
        }

        var updated = false;
        foreach (var row in configSheet.RowsUsed())
        {
            if (row.Cell(1).GetString().Trim() != "产线配置版本")
            {
                continue;
            }

            row.Cell(2).Value = revision.ToString();
            updated = true;
            break;
        }

        if (!updated)
        {
            var nextRow = configSheet.LastRowUsed()?.RowNumber() + 1 ?? 2;
            configSheet.Cell(nextRow, 1).Value = "产线配置版本";
            configSheet.Cell(nextRow, 2).Value = revision.ToString();
        }

        workbook.SaveAs(filePath);
    }

    public static int ReadLineConfigRevision(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return 0;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return ReadLineConfigRevision(stream);
        }
        catch
        {
            return 0;
        }
    }

    public static int ReadLineConfigRevision(Stream stream)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
            {
                return 0;
            }

            var map = ReadKeyValueSheet(configSheet);
            return GetInt(map, "产线配置版本", 0);
        }
        catch
        {
            return 0;
        }
    }

    public static void ImportToLineFile(string sourcePath, string destinationPath, string lineName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var sourceWorkbook = new XLWorkbook(sourcePath);
        if (MqttFieldMappingImporter.IsFieldMappingWorkbook(sourceWorkbook))
        {
            EnsureLineFile(destinationPath, lineName);
            MergeFieldMapping(sourcePath, destinationPath, lineName);
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        EnsureLineFile(destinationPath, lineName);
    }

    public static void MergeFieldMapping(string mappingSourcePath, string lineFilePath, string lineName)
    {
        EnsureLineFile(lineFilePath, lineName);

        var settings = new AppSettings();
        Apply(settings, lineFilePath);

        using var mappingWorkbook = new XLWorkbook(mappingSourcePath);
        MqttFieldMappingImporter.Apply(settings, mappingWorkbook);
        Export(settings, lineFilePath);
    }

    public static AppSettings CreateSeedSettings(string lineName)
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, lineName);
        return settings;
    }

    /// <summary>
    /// 仅修改点表中的「运行状态」类型，以及配置表中的「精度」项；不重写整本 Excel。
    /// </summary>
    public static bool PatchRunStatusAndPrecision(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var workbook = new XLWorkbook(filePath);
        var changed = false;

        if (workbook.Worksheets.TryGetWorksheet(TagsSheetName, out var tagsSheet))
        {
            var lastRow = tagsSheet.LastRowUsed()?.RowNumber() ?? 1;
            for (var row = 2; row <= lastRow; row++)
            {
                if (!tagsSheet.Cell(row, 1).GetString().Trim().Equals(RunStatusFormatting.TagName, StringComparison.Ordinal))
                {
                    continue;
                }

                var typeCell = tagsSheet.Cell(row, 4);
                if (!typeCell.GetString().Trim().Equals(nameof(TagDataType.Int16), StringComparison.OrdinalIgnoreCase))
                {
                    typeCell.Value = nameof(TagDataType.Int16);
                    changed = true;
                }

                var categoryColumn = FindTagColumn(tagsSheet, "显示分组");
                if (categoryColumn > 0)
                {
                    var categoryCell = tagsSheet.Cell(row, categoryColumn);
                    var switchLabel = TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Switch);
                    if (!categoryCell.GetString().Trim().Equals(switchLabel, StringComparison.Ordinal))
                    {
                        categoryCell.Value = switchLabel;
                        changed = true;
                    }
                }

                break;
            }
        }

        if (workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            foreach (var row in configSheet.RowsUsed())
            {
                var key = row.Cell(1).GetString().Trim();
                if (!key.Equals("温度精度", StringComparison.Ordinal))
                {
                    continue;
                }

                row.Cell(1).Value = "精度";
                changed = true;
            }
        }

        if (changed)
        {
            workbook.SaveAs(filePath);
        }

        return changed;
    }

    public static void MergeMissingCatalogTags(AppSettings settings, IReadOnlyList<PlcTag> catalogTags)
    {
        var existing = settings.Tags.ToDictionary(tag => tag.Name, StringComparer.Ordinal);
        foreach (var catalogTag in catalogTags)
        {
            if (existing.ContainsKey(catalogTag.Name))
            {
                continue;
            }

            settings.Tags.Add(new PlcTag
            {
                Name = catalogTag.Name,
                Unit = catalogTag.Unit,
                XinjeAddress = catalogTag.XinjeAddress,
                DataType = catalogTag.DataType,
                ByteOrder = catalogTag.ByteOrder,
                Source = catalogTag.Source,
                ManualValue = catalogTag.ManualValue,
                DisplayPrecision = catalogTag.DisplayPrecision,
                MqttField = catalogTag.MqttField,
                DisplayCategory = catalogTag.DisplayCategory,
                Enabled = catalogTag.Enabled
            });
        }
    }

    static bool NeedsFormatUpgrade(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            return true;
        }

        var map = ReadKeyValueSheet(configSheet);
        var version = GetInt(map, "配置版本", 0);
        if (version < FormatVersion)
        {
            return true;
        }

        return !workbook.Worksheets.TryGetWorksheet(MqttSheetName, out _) ||
               !workbook.Worksheets.TryGetWorksheet(FieldMappingSheetName, out _) ||
               !workbook.Worksheets.TryGetWorksheet(TagsSheetName, out _) ||
               !HasDisplayCategoryColumn(workbook);
    }

    static bool NeedsRevisionUpgrade(string filePath) =>
        ReadLineConfigRevision(filePath) < LineCatalog.Version;

    static bool HasDisplayCategoryColumn(XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(TagsSheetName, out var sheet))
        {
            return false;
        }

        return FindTagColumn(sheet, "显示分组") > 0;
    }

    static void TryApplyExistingSheets(AppSettings settings, XLWorkbook workbook)
    {
        if (workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out _))
        {
            ApplyConfigSheet(settings, workbook);
        }

        if (workbook.Worksheets.TryGetWorksheet(MqttSheetName, out _))
        {
            ApplyMqttPayloadSheet(settings, workbook);
        }

        if (workbook.Worksheets.TryGetWorksheet(TagsSheetName, out _))
        {
            settings.Tags = ReadTagsSheet(workbook);
        }

        ApplyFieldMappings(settings, workbook);
    }

    static void ApplyFieldMappings(AppSettings settings, XLWorkbook workbook)
    {
        if (MqttFieldMappingImporter.ApplyFromWorkbookTags(settings, workbook) > 0)
        {
            return;
        }

        if (workbook.Worksheets.TryGetWorksheet(TagsSheetName, out var tagsSheet))
        {
            ApplyLegacyTagMqttFields(settings.Tags, tagsSheet);
        }
    }

    static void ApplyLegacyTagMqttFields(IList<PlcTag> tags, IXLWorksheet sheet)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? TagHeaders.Length;
        if (lastColumn < 12)
        {
            return;
        }

        var header = sheet.Cell(1, 12).GetString().Trim();
        if (!header.Contains("MQTT", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        var byName = tags.ToDictionary(tag => tag.Name, StringComparer.Ordinal);
        for (var row = 2; row <= lastRow; row++)
        {
            var name = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name) || !byName.TryGetValue(name, out var tag))
            {
                continue;
            }

            tag.MqttField = sheet.Cell(row, 12).GetString().Trim();
        }
    }

    static void WriteConfigSheet(XLWorkbook workbook, AppSettings settings)
    {
        var sheet = workbook.Worksheets.Add(ConfigSheetName);
        sheet.Cell(1, 1).Value = "配置项";
        sheet.Cell(1, 2).Value = "值";
        sheet.Cell(1, 3).Value = "说明";
        sheet.Row(1).Style.Font.Bold = true;

        var values = BuildConfigMap(settings);
        var row = 2;
        foreach (var (key, label) in ConfigRows)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = values[key];
            sheet.Cell(row, 3).Value = key;
            row++;
        }

        sheet.Columns(1, 3).AdjustToContents();
    }

    static void WriteMqttPayloadSheet(XLWorkbook workbook, MqttPayloadProfile profile)
    {
        var sheet = workbook.Worksheets.Add(MqttSheetName);
        sheet.Cell(1, 1).Value = "配置项";
        sheet.Cell(1, 2).Value = "值";
        sheet.Cell(1, 3).Value = "说明";
        sheet.Row(1).Style.Font.Bold = true;

        var values = BuildMqttPayloadMap(profile);
        var row = 2;
        foreach (var (key, label, hint) in MqttPayloadRows)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = values[key];
            sheet.Cell(row, 3).Value = hint;
            row++;
        }

        sheet.Columns(1, 3).AdjustToContents();
    }

    static Dictionary<string, string> BuildMqttPayloadMap(MqttPayloadProfile profile) => new(StringComparer.Ordinal)
    {
        ["PayloadFormat"] = profile.PayloadFormat,
        ["TagsPath"] = profile.TagsPath,
        ["DeviceIdPath"] = profile.DeviceIdPath,
        ["TimestampPath"] = profile.TimestampPath,
        ["TimestampFormat"] = profile.TimestampFormat,
        ["QualityPath"] = profile.QualityPath,
        ["PlcHostPath"] = profile.PlcHostPath,
        ["SimulatorPath"] = profile.SimulatorPath,
        ["UseTagNameWhenFieldEmpty"] = profile.UseTagNameWhenFieldEmpty ? "是" : "否",
    };

    static void ApplyMqttPayloadSheet(AppSettings settings, XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(MqttSheetName, out var sheet))
        {
            return;
        }

        var map = ReadKeyValueSheet(sheet);
        var profile = settings.MqttPayload ?? new MqttPayloadProfile();
        profile.PayloadFormat = GetString(map, "报文格式", profile.PayloadFormat);
        profile.TagsPath = GetOptionalString(map, "点位容器路径", profile.TagsPath);
        profile.DeviceIdPath = GetOptionalString(map, "设备ID字段", profile.DeviceIdPath);
        profile.TimestampPath = GetOptionalString(map, "时间戳字段", profile.TimestampPath);
        profile.TimestampFormat = GetString(map, "时间戳格式", profile.TimestampFormat);
        profile.QualityPath = GetOptionalString(map, "质量字段", profile.QualityPath);
        profile.PlcHostPath = GetOptionalString(map, "PLC地址字段", profile.PlcHostPath);
        profile.SimulatorPath = GetOptionalString(map, "模拟模式字段", profile.SimulatorPath);
        profile.UseTagNameWhenFieldEmpty = GetBool(map, "未映射时用点位名称", profile.UseTagNameWhenFieldEmpty);
        MqttFieldMappingCatalog.NormalizeLegacyProfile(profile);
        settings.MqttPayload = profile;
    }

    static Dictionary<string, string> BuildConfigMap(AppSettings settings) => new(StringComparer.Ordinal)
    {
        ["ExcelFormatVersion"] = FormatVersion.ToString(),
        ["LineConfigRevision"] = LineCatalog.Version.ToString(),
        ["LineName"] = settings.LineName,
        ["DeviceId"] = settings.DeviceId,
        ["PlcModel"] = settings.Plc.Model,
        ["PlcHost"] = settings.Plc.Host,
        ["PlcPort"] = settings.Plc.Port.ToString(),
        ["PlcStation"] = settings.Plc.Station.ToString(),
        ["PlcTimeoutMs"] = settings.Plc.TimeoutMs.ToString(),
        ["ScanIntervalMs"] = settings.ScanIntervalMs.ToString(),
        ["TemperaturePublishThresholdC"] = settings.TemperaturePublishThresholdC.ToString("G"),
        ["TemperaturePrecision"] = settings.TemperaturePrecision.ToString(),
        ["UseSimulator"] = settings.UseSimulator ? "是" : "否",
        ["MqttHost"] = settings.Mqtt.Host,
        ["MqttPort"] = settings.Mqtt.Port.ToString(),
        ["MqttClientId"] = settings.Mqtt.ClientId,
        ["MqttUsername"] = settings.Mqtt.Username,
        ["MqttPassword"] = settings.Mqtt.Password,
        ["MqttUseTls"] = settings.Mqtt.UseTls ? "是" : "否",
        ["MqttQos"] = settings.Mqtt.Qos.ToString(),
        ["MqttTopic"] = settings.Mqtt.Topic,
        ["SubscribeTopics"] = string.Join(';', settings.SubscribeTopics),
        ["OperationMode"] = FormatOperationMode(settings.OperationMode),
        ["StartWithWindows"] = settings.StartWithWindows ? "是" : "否",
        ["AutoStartAcquisition"] = settings.AutoStartAcquisition ? "是" : "否",
        ["EnableHistoryRecording"] = settings.EnableHistoryRecording ? "是" : "否",
        ["HistoryRetentionDays"] = settings.HistoryRetentionDays.ToString(),
    };

    static void WriteTagsSheet(XLWorkbook workbook, IReadOnlyList<PlcTag> tags)
    {
        var sheet = workbook.Worksheets.Add(TagsSheetName);
        for (var i = 0; i < TagHeaders.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = TagHeaders[i];
        }

        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var tag in tags)
        {
            sheet.Cell(row, 1).Value = tag.Name;
            sheet.Cell(row, 2).Value = tag.Source == TagSource.Manual ? "手动" : "PLC";
            sheet.Cell(row, 3).Value = tag.Source == TagSource.Manual ? string.Empty : tag.XinjeAddress;
            sheet.Cell(row, 4).Value = tag.DataType.ToString();
            sheet.Cell(row, 5).Value = tag.Unit;
            sheet.Cell(row, 6).Value = tag.ByteOrder.ToString();
            sheet.Cell(row, 7).Value = tag.Enabled ? "是" : "否";
            sheet.Cell(row, 8).Value = tag.ManualValue;
            sheet.Cell(row, 9).Value = tag.DisplayPrecision?.ToString() ?? string.Empty;
            sheet.Cell(row, 10).Value = tag.Scale;
            sheet.Cell(row, 11).Value = tag.Offset;
            sheet.Cell(row, 12).Value = TagDisplayCategoryHelper.ToLabel(
                tag.DisplayCategory ?? TagDisplayCategoryHelper.InferCategory(tag));
            sheet.Cell(row, 13).Value = tag.UseScannerInput ? "是" : "否";
            row++;
        }

        sheet.Columns(1, TagHeaders.Length).AdjustToContents();
    }

    static void WriteDisplayCategorySheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add(DisplayCategorySheetName);
        sheet.Cell(1, 1).Value = "显示分组";
        sheet.Cell(1, 2).Value = "说明";
        sheet.Cell(1, 3).Value = "主题色";
        sheet.Cell(1, 4).Value = "监控页表现";
        sheet.Row(1).Style.Font.Bold = true;

        var rows = new (TagDisplayCategory Category, string Description, string Presentation)[]
        {
            (TagDisplayCategory.Switch, "Bool / 运行停止类点位", "大圆点 + 运行中/已停止，绿/红高亮"),
            (TagDisplayCategory.Temperature, "名称含「温度」或单位 ℃", "橙色分组 + 左侧色条"),
            (TagDisplayCategory.Process, "车速、间隙、张力等工艺数值", "青色分组 + 左侧色条"),
            (TagDisplayCategory.Setting, "手动输入：型号、货号、门幅、厚度等", "蓝色分组 + 左侧色条"),
            (TagDisplayCategory.Other, "未归入以上分组的点位", "灰色分组 + 左侧色条"),
        };

        var row = 2;
        foreach (var (category, description, presentation) in rows)
        {
            sheet.Cell(row, 1).Value = TagDisplayCategoryHelper.ToLabel(category);
            sheet.Cell(row, 2).Value = description;
            sheet.Cell(row, 3).Value = TagDisplayCategoryHelper.GetAccentColor(category);
            sheet.Cell(row, 4).Value = presentation;
            row++;
        }

        sheet.Cell(row + 1, 1).Value = "用法";
        sheet.Cell(row + 2, 1).Value = "在「点表」工作表的「显示分组」列填写上表分组名称；留空则程序按数据类型自动推断。";
        sheet.Columns(1, 4).AdjustToContents();
    }

    static void WriteFieldMappingSheet(XLWorkbook workbook, IReadOnlyList<PlcTag> tags)
    {
        var sheet = workbook.Worksheets.Add(FieldMappingSheetName);
        sheet.Cell(1, 1).Value = "id";
        sheet.Cell(1, 2).Value = "name";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var tag in tags.Where(tag => tag.Enabled))
        {
            sheet.Cell(row, 1).Value = tag.MqttField;
            sheet.Cell(row, 2).Value = tag.Name;
            row++;
        }

        sheet.Columns(1, 2).AdjustToContents();
    }

    static void ApplyConfigSheet(
        AppSettings settings,
        XLWorkbook workbook,
        string? expectedLineName = null,
        string? templateFilePath = null)
    {
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var sheet))
        {
            throw new InvalidOperationException($"Excel 缺少工作表「{ConfigSheetName}」。");
        }

        var map = ReadKeyValueSheet(sheet);
        var lineName = expectedLineName
                       ?? GetString(map, "产线名称", settings.LineName);
        var defaults = ReadConfigDefaults(lineName, templateFilePath);

        settings.LineName = lineName;
        settings.DeviceId = GetString(map, "设备编号", defaults.DeviceId);
        settings.Plc.Model = GetString(map, "PLC型号", defaults.Plc.Model);
        settings.Plc.Host = GetString(map, "PLC_IP", defaults.Plc.Host);
        settings.Plc.Port = GetInt(map, "PLC端口", defaults.Plc.Port);
        settings.Plc.Station = (byte)GetInt(map, "PLC站号", defaults.Plc.Station);
        settings.Plc.TimeoutMs = GetInt(map, "PLC超时毫秒", defaults.Plc.TimeoutMs);
        settings.ScanIntervalMs = GetInt(map, "扫描周期毫秒", defaults.ScanIntervalMs);
        settings.TemperaturePublishThresholdC = GetDouble(map, "温度发布阈值", defaults.TemperaturePublishThresholdC);
        settings.TemperaturePrecision = GetIntPreferring(map, "精度", "温度精度", defaults.TemperaturePrecision);
        settings.UseSimulator = GetBool(map, "使用模拟数据", defaults.UseSimulator);
        settings.Mqtt.Host = GetString(map, "MQTT_Broker", defaults.Mqtt.Host);
        settings.Mqtt.Port = GetInt(map, "MQTT端口", defaults.Mqtt.Port);
        settings.Mqtt.ClientId = GetString(map, "MQTT_ClientId", defaults.Mqtt.ClientId);
        settings.Mqtt.Username = GetString(map, "MQTT_用户名", defaults.Mqtt.Username);
        settings.Mqtt.Password = GetString(map, "MQTT_密码", defaults.Mqtt.Password);
        settings.Mqtt.UseTls = GetBool(map, "MQTT_TLS", defaults.Mqtt.UseTls);
        settings.Mqtt.Qos = GetInt(map, "MQTT_QoS", defaults.Mqtt.Qos);
        settings.Mqtt.Topic = GetString(map, "MQTT发布主题", defaults.Mqtt.Topic);
        if (string.IsNullOrWhiteSpace(settings.Mqtt.ClientId))
        {
            settings.Mqtt.ClientId = LineMqttDefaults.ResolveClientIdForLine(settings.LineName);
        }

        settings.OperationMode = ParseOperationMode(GetString(map, "运行模式", FormatOperationMode(defaults.OperationMode)));
        settings.StartWithWindows = GetBool(map, "开机自动启动", defaults.StartWithWindows);
        settings.AutoStartAcquisition = GetBool(map, "启动后自动运行", defaults.AutoStartAcquisition);
        settings.EnableHistoryRecording = GetBool(map, "记录历史数据", defaults.EnableHistoryRecording);
        settings.HistoryRetentionDays = GetInt(map, "历史保留天数", defaults.HistoryRetentionDays);

        ApplySubscribeTopics(settings, map);
    }

    static AppSettings ReadConfigDefaults(string lineName, string? templateFilePath)
    {
        if (string.IsNullOrWhiteSpace(templateFilePath) || !File.Exists(templateFilePath))
        {
            return new AppSettings { LineName = lineName };
        }

        using var workbook = new XLWorkbook(templateFilePath);
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var sheet))
        {
            return new AppSettings { LineName = lineName };
        }

        var map = ReadKeyValueSheet(sheet);
        return new AppSettings
        {
            LineName = lineName,
            DeviceId = GetString(map, "设备编号", lineName),
            Plc = new PlcSettings
            {
                Model = GetString(map, "PLC型号", "XD5E-60T10"),
                Host = GetString(map, "PLC_IP", "192.168.6.10"),
                Port = GetInt(map, "PLC端口", 502),
                Station = (byte)GetInt(map, "PLC站号", 1),
                TimeoutMs = GetInt(map, "PLC超时毫秒", 2000)
            },
            Mqtt = new MqttSettings
            {
                Host = GetString(map, "MQTT_Broker", LineMqttDefaults.Host),
                Port = GetInt(map, "MQTT端口", LineMqttDefaults.Port),
                ClientId = GetString(map, "MQTT_ClientId", LineMqttDefaults.ResolveClientIdForLine(lineName)),
                Username = GetString(map, "MQTT_用户名", LineMqttDefaults.Username),
                Password = GetString(map, "MQTT_密码", LineMqttDefaults.Password),
                UseTls = GetBool(map, "MQTT_TLS", false),
                Qos = GetInt(map, "MQTT_QoS", 0),
                Topic = GetString(map, "MQTT发布主题", LineMqttDefaults.ResolvePublishTopic(lineName))
            }
        };
    }

    static string? ReadConfigLineName(XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var sheet))
        {
            return null;
        }

        var map = ReadKeyValueSheet(sheet);
        return map.TryGetValue("产线名称", out var lineName) && !string.IsNullOrWhiteSpace(lineName)
            ? lineName.Trim()
            : null;
    }

    static bool ConfigSheetMatchesLine(XLWorkbook workbook, string lineName)
    {
        var fileLineName = ReadConfigLineName(workbook);
        return string.IsNullOrWhiteSpace(fileLineName) ||
               string.Equals(fileLineName, lineName, StringComparison.Ordinal);
    }

    static void CopyPreserveFrom(AppSettings? preserveFrom, AppSettings settings)
    {
        if (preserveFrom is null)
        {
            return;
        }

        settings.OperationMode = preserveFrom.OperationMode;
        settings.SubscribeTopics = preserveFrom.SubscribeTopics.ToList();
        settings.SubscribeTopic = preserveFrom.SubscribeTopic;
        settings.StartWithWindows = preserveFrom.StartWithWindows;
        settings.AutoStartAcquisition = preserveFrom.AutoStartAcquisition;
        settings.EnableHistoryRecording = preserveFrom.EnableHistoryRecording;
        settings.HistoryRetentionDays = preserveFrom.HistoryRetentionDays;
    }

    static string FormatOperationMode(AppOperationMode mode) =>
        mode == AppOperationMode.Subscribe ? "订阅模式" : "采集模式";

    static AppOperationMode ParseOperationMode(string text) =>
        text.Contains("订阅", StringComparison.Ordinal)
            ? AppOperationMode.Subscribe
            : AppOperationMode.Acquisition;

    static void ApplySubscribeTopics(AppSettings settings, IReadOnlyDictionary<string, string> map)
    {
        if (!map.TryGetValue("订阅主题", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var topics = raw
            .Split([';', '；', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .ToList();
        if (topics.Count == 0)
        {
            return;
        }

        settings.SubscribeTopics = topics;
        settings.SubscribeTopic = topics[0];
    }

    static List<PlcTag> ReadTagsSheet(XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(TagsSheetName, out var sheet))
        {
            throw new InvalidOperationException($"Excel 缺少工作表「{TagsSheetName}」。");
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (lastRow < 2)
        {
            return [];
        }

        var tags = new List<PlcTag>();
        var displayCategoryColumn = FindTagColumn(sheet, "显示分组");
        for (var row = 2; row <= lastRow; row++)
        {
            var name = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var sourceText = sheet.Cell(row, 2).GetString().Trim();
            var source = sourceText is "手动" or "Manual"
                ? TagSource.Manual
                : TagSource.Plc;
            var address = sheet.Cell(row, 3).GetString().Trim();
            var dataType = ParseEnum(sheet.Cell(row, 4).GetString(), TagDataType.Float32);
            var unit = sheet.Cell(row, 5).GetString().Trim();
            var byteOrder = ParseEnum(sheet.Cell(row, 6).GetString(), ByteOrder.CDAB);
            var enabled = ParseBoolCell(sheet.Cell(row, 7), true);
            var manualValue = sheet.Cell(row, 8).GetString().Trim();
            var precisionText = sheet.Cell(row, 9).GetString().Trim();
            int? precision = int.TryParse(precisionText, out var parsedPrecision) ? parsedPrecision : null;
            var scale = ParseDoubleCell(sheet.Cell(row, 10), 1);
            var offset = ParseDoubleCell(sheet.Cell(row, 11), 0);

            var tag = new PlcTag
            {
                Name = name,
                Source = source,
                XinjeAddress = string.IsNullOrWhiteSpace(address) ? "D0" : address,
                DataType = dataType,
                Unit = unit,
                ByteOrder = byteOrder,
                Enabled = enabled,
                ManualValue = manualValue,
                DisplayPrecision = precision,
                Scale = scale,
                Offset = offset
            };

            if (displayCategoryColumn > 0)
            {
                var categoryText = sheet.Cell(row, displayCategoryColumn).GetString();
                if (TagDisplayCategoryHelper.TryParseLabel(categoryText, out var category))
                {
                    tag.DisplayCategory = category;
                }
            }

            var scannerInputColumn = FindTagColumn(sheet, "扫码输入");
            if (scannerInputColumn > 0)
            {
                tag.UseScannerInput = ParseBoolCell(sheet.Cell(row, scannerInputColumn), false);
            }
            else if (string.Equals(name, LineCatalog.ProductSkuTagName, StringComparison.Ordinal))
            {
                tag.UseScannerInput = true;
            }

            if (tag.Source != TagSource.Manual)
            {
                XinjeXd5eMapper.ApplyTo(tag);
            }

            tags.Add(tag);
        }

        if (tags.Count == 0)
        {
            throw new InvalidOperationException("点表为空，请至少保留一行点位。");
        }

        return tags;
    }

    static int FindTagColumn(IXLWorksheet sheet, string headerName)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var column = 1; column <= lastColumn; column++)
        {
            if (sheet.Cell(1, column).GetString().Trim().Equals(headerName, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return -1;
    }

    static Dictionary<string, string> ReadKeyValueSheet(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var key = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            map[key] = sheet.Cell(row, 2).GetString().Trim();
        }

        return map;
    }

    static string GetString(IReadOnlyDictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    static string GetOptionalString(IReadOnlyDictionary<string, string> map, string key, string fallback)
    {
        if (!map.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Trim();
    }

    static int GetInt(IReadOnlyDictionary<string, string> map, string key, int fallback) =>
        map.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    static int GetIntPreferring(IReadOnlyDictionary<string, string> map, string key, string legacyKey, int fallback) =>
        map.ContainsKey(key) ? GetInt(map, key, fallback) : GetInt(map, legacyKey, fallback);

    static double GetDouble(IReadOnlyDictionary<string, string> map, string key, double fallback) =>
        map.TryGetValue(key, out var value) && double.TryParse(value, out var parsed) ? parsed : fallback;

    static bool GetBool(IReadOnlyDictionary<string, string> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return ParseBoolText(value, fallback);
    }

    static bool ParseBoolCell(IXLCell cell, bool fallback)
    {
        if (cell.TryGetValue(out bool boolValue))
        {
            return boolValue;
        }

        return ParseBoolText(cell.GetString(), fallback);
    }

    static bool ParseBoolText(string text, bool fallback) =>
        text.Trim() switch
        {
            "是" or "true" or "True" or "1" or "Y" or "yes" => true,
            "否" or "false" or "False" or "0" or "N" or "no" => false,
            "" => fallback,
            _ => fallback
        };

    static TEnum ParseEnum<TEnum>(string text, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(text.Trim(), true, out var parsed) ? parsed : fallback;

    static double ParseDoubleCell(IXLCell cell, double fallback)
    {
        if (cell.TryGetValue(out double value))
        {
            return value;
        }

        return double.TryParse(cell.GetString(), out var parsed) ? parsed : fallback;
    }
}
