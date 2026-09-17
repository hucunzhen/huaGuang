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
    public const string MqttEndpointsSheetName = "MQTT目标";
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
        ("PlcProtocol", "PLC协议"),
        ("PlcHost", "PLC_IP"),
        ("PlcPort", "PLC端口"),
        ("PlcStation", "PLC站号"),
        ("PlcRack", "PLC机架号"),
        ("PlcSlot", "PLC槽位"),
        ("PlcCpuType", "PLC_CPU类型"),
        ("PlcTimeoutMs", "PLC超时毫秒"),
        ("ScanIntervalMs", "扫描周期毫秒"),
        ("PublishIntervalMs", "发布周期毫秒"),
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
        ("LogExportDirectory", "日志打包目录"),
    ];

    public static void Export(AppSettings settings, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, settings.LineName);

        using var workbook = new XLWorkbook();
        MqttEndpointCatalog.PrepareForExport(settings);
        MqttEndpointCatalog.Normalize(settings);
        WriteConfigSheet(workbook, settings);
        WriteMqttEndpointsSheet(workbook, settings);
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
        ApplyWorkbook(settings, workbook, expectedLineName);
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
        if (NeedsShippedTemplateRefresh(filePath, lineName, templateFilePath))
        {
            useTemplate = true;
        }
        else if (File.Exists(filePath))
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
        string? expectedLineName = null)
    {
        ValidateWorkbookFormat(workbook);
        settings.ConfigLoadWarnings.Clear();
        ApplyConfigSheet(settings, workbook, expectedLineName);
        ApplyMqttEndpointsSheet(settings, workbook);
        MqttEndpointCatalog.Normalize(settings);
        ApplyMqttPayloadSheet(settings, workbook);
        settings.Tags = ReadTagsSheet(workbook, settings.Plc.Protocol, settings.ConfigLoadWarnings);
        PlcTagIdentity.AssignStableIds(settings);
        ApplyFieldMappings(settings, workbook);
    }

    public static void EnsureLineFile(string filePath, string lineName, string? templateFilePath = null)
    {
        EnsureLineFileExists(filePath, lineName, templateFilePath);
    }

    /// <summary>
    /// 为已有产线 Excel 补写「MQTT目标」工作表（从「配置」页 MQTT 项读取，不改动其他 sheet）。
    /// </summary>
    /// <returns>已写入返回 true；工作表已存在则返回 false。</returns>
    public static bool PatchMqttEndpointsInFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var workbook = new XLWorkbook(filePath);
        if (workbook.Worksheets.Contains(MqttEndpointsSheetName))
        {
            return false;
        }

        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{ConfigSheetName}」。");
        }

        var map = ReadKeyValueSheet(configSheet);
        var endpoint = new MqttEndpoint
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "默认",
            Enabled = true,
            Host = GetString(map, "MQTT_Broker", LineMqttDefaults.Host),
            Port = GetInt(map, "MQTT端口", LineMqttDefaults.Port),
            ClientId = GetString(map, "MQTT_ClientId", string.Empty),
            Username = GetString(map, "MQTT_用户名", LineMqttDefaults.Username),
            Password = GetString(map, "MQTT_密码", LineMqttDefaults.Password),
            UseTls = GetBool(map, "MQTT_TLS", false),
            Qos = GetInt(map, "MQTT_QoS", 0),
            Topic = GetString(map, "MQTT发布主题", LineMqttDefaults.XianhePublishTopic)
        };

        WriteMqttEndpointsSheet(workbook, new AppSettings { MqttEndpoints = [endpoint] });
        workbook.SaveAs(filePath);
        return true;
    }

    /// <summary>
    /// 产线 Excel 已存在时只做 catalog 同步类维护，不重写整本 Excel。
    /// </summary>
    public static void ApplyLineFileMaintenance(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        PatchCurrentInjectionDisplayGroup(filePath);
        RemoveDeprecatedTagsFromFile(filePath);
        MergeMissingCatalogTagsIntoFile(filePath);
        PatchEmptyMqttFieldMappings(filePath);
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

    public static bool RemoveDeprecatedTagsFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            return false;
        }

        var map = ReadKeyValueSheet(configSheet);
        var lineName = GetString(map, "产线名称", string.Empty);
        if (string.IsNullOrWhiteSpace(lineName))
        {
            return false;
        }

        var settings = LoadLineExcelFromFile(filePath, templateFilePath: null, expectedLineName: lineName);
        var removed = DeprecatedLineTags.RemoveFrom(settings.Tags);
        if (removed == 0)
        {
            return false;
        }

        Export(settings, filePath);
        return true;
    }

    public static bool MergeMissingCatalogTagsIntoFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            return false;
        }

        var map = ReadKeyValueSheet(configSheet);
        var lineName = GetString(map, "产线名称", string.Empty);
        if (string.IsNullOrWhiteSpace(lineName))
        {
            return false;
        }

        var settings = LoadLineExcelFromFile(filePath, templateFilePath: null, expectedLineName: lineName);
        var before = settings.Tags.Count;
        MergeMissingRequiredPlcTags(settings, LineCatalog.Resolve(lineName).Tags);
        MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, lineName);
        if (settings.Tags.Count <= before)
        {
            return false;
        }

        Export(settings, filePath);
        return true;
    }

    public static bool PatchCurrentInjectionDisplayGroup(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet(TagsSheetName, out var tagsSheet))
        {
            return false;
        }

        var categoryColumn = FindTagColumn(tagsSheet, "显示分组");
        if (categoryColumn <= 0)
        {
            return false;
        }

        var groupLabel = TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Temperature);
        var changed = false;
        var lastRow = tagsSheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var name = tagsSheet.Cell(row, 1).GetString().Trim();
            if (!CurrentInjectionFormatting.IsRelatedTag(new PlcTag { Name = name }))
            {
                continue;
            }

            var categoryCell = tagsSheet.Cell(row, categoryColumn);
            if (categoryCell.GetString().Trim().Equals(groupLabel, StringComparison.Ordinal))
            {
                continue;
            }

            categoryCell.Value = groupLabel;
            changed = true;
        }

        if (changed)
        {
            workbook.SaveAs(filePath);
        }

        return changed;
    }

    /// <summary>补全「字段映射」表中空的 id 列，不重写点表。</summary>
    public static bool PatchEmptyMqttFieldMappings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet(FieldMappingSheetName, out var mappingSheet))
        {
            return false;
        }

        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            return false;
        }

        var map = ReadKeyValueSheet(configSheet);
        var lineName = GetString(map, "产线名称", string.Empty);
        if (string.IsNullOrWhiteSpace(lineName))
        {
            lineName = Path.GetFileNameWithoutExtension(filePath);
        }

        var settings = new AppSettings { LineName = lineName };
        if (workbook.Worksheets.TryGetWorksheet(TagsSheetName, out _))
        {
            var protocol = PlcSettingsHelper.ParseProtocol(GetString(map, "PLC协议", string.Empty), GetString(map, "PLC型号", string.Empty));
            settings.Tags = ReadTagsSheet(workbook, protocol, settings.ConfigLoadWarnings);
        }

        ApplyFieldMappings(settings, workbook);

        var before = settings.Tags.ToDictionary(tag => tag.Name, tag => tag.MqttField, StringComparer.Ordinal);
        MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, lineName);
        var changed = settings.Tags.Any(tag =>
            !string.Equals(before.GetValueOrDefault(tag.Name), tag.MqttField, StringComparison.Ordinal));

        if (!changed)
        {
            return false;
        }

        var idColumn = FindMappingColumn(mappingSheet, "id", "mqtt字段", "mqtt_field", "字段", "键");
        var nameColumn = FindMappingColumn(mappingSheet, "name", "名称", "点位名称", "点位", "点表名称");
        if (idColumn <= 0 || nameColumn <= 0)
        {
            return false;
        }

        var mqttByName = settings.Tags.ToDictionary(tag => tag.Name, tag => tag.MqttField, StringComparer.Ordinal);
        var lastRow = mappingSheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var name = mappingSheet.Cell(row, nameColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!mqttByName.TryGetValue(name, out var mqttField) ||
                string.IsNullOrWhiteSpace(mqttField))
            {
                continue;
            }

            var idCell = mappingSheet.Cell(row, idColumn);
            if (!string.IsNullOrWhiteSpace(idCell.GetString()))
            {
                continue;
            }

            idCell.Value = mqttField;
        }

        workbook.SaveAs(filePath);
        return true;
    }

    public static void ExportReferenceFieldMapping(string filePath, string lineName = "先河热熔胶复合机")
    {
        var settings = CreateSeedSettings(lineName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        using var workbook = new XLWorkbook();
        WriteFieldMappingSheet(workbook, settings.Tags);
        workbook.SaveAs(filePath);
    }

    static int FindMappingColumn(IXLWorksheet sheet, params string[] candidates)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = sheet.Cell(1, col).GetString().Trim();
            if (candidates.Any(candidate =>
                    string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return col;
            }
        }

        return -1;
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

            var added = new PlcTag
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
            };
            PlcTagIdentity.AssignStableId(added, settings.LineName);
            settings.Tags.Add(added);
        }
    }

    /// <summary>仅补全 catalog 新增且必须存在的 PLC 点位，不恢复用户已删的手动点位。</summary>
    public static void MergeMissingRequiredPlcTags(AppSettings settings, IReadOnlyList<PlcTag> catalogTags)
    {
        var requiredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "当前注胶机编号",
            "当前工作胶盘温度",
            "当前工作胶管温度",
            "当前工作胶枪温度"
        };
        if (string.Equals(settings.LineName, "华迪热熔胶复合机", StringComparison.Ordinal))
        {
            requiredNames.Add("上展开转速率");
            requiredNames.Add("下展开转速率");
            requiredNames.Add("注胶量");
        }

        var required = catalogTags.Where(tag => requiredNames.Contains(tag.Name)).ToList();
        MergeMissingCatalogTags(settings, required);
    }

    static void ValidateWorkbookFormat(XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var configSheet))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{ConfigSheetName}」。");
        }

        var map = ReadKeyValueSheet(configSheet);
        var version = GetInt(map, "配置版本", 0);
        if (version != FormatVersion)
        {
            throw new InvalidOperationException(
                $"产线 Excel 配置版本为 {version}，当前仅支持版本 {FormatVersion}。请使用安装包自带的产线模板或在应用内重新导出。");
        }

        if (!workbook.Worksheets.TryGetWorksheet(MqttSheetName, out _))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{MqttSheetName}」。");
        }

        if (!workbook.Worksheets.TryGetWorksheet(FieldMappingSheetName, out _))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{FieldMappingSheetName}」。");
        }

        if (!workbook.Worksheets.TryGetWorksheet(TagsSheetName, out _))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{TagsSheetName}」。");
        }

        if (!HasDisplayCategoryColumn(workbook))
        {
            throw new InvalidOperationException("产线 Excel「点表」缺少「显示分组」列。");
        }
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

    static void ApplyFieldMappings(AppSettings settings, XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(FieldMappingSheetName, out _))
        {
            throw new InvalidOperationException($"产线 Excel 缺少工作表「{FieldMappingSheetName}」。");
        }

        var applied = MqttFieldMappingImporter.ApplyFromWorkbookTags(settings, workbook);
        if (applied == 0)
        {
            MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, settings.LineName);
            applied = settings.Tags.Count(tag => !string.IsNullOrWhiteSpace(tag.MqttField));
        }

        if (applied == 0)
        {
            throw new InvalidOperationException($"产线 Excel「{FieldMappingSheetName}」中没有有效的 id/name 映射行。");
        }
    }

    /// <summary>
    /// Android 等场景若曾用空种子生成过 Excel，点表为空；有安装包模板时应重新解压覆盖。
    /// </summary>
    public static bool NeedsShippedTemplateRefresh(string filePath, string lineName, string? templateFilePath)
    {
        if (string.IsNullOrWhiteSpace(templateFilePath) || !File.Exists(templateFilePath))
        {
            return false;
        }

        if (!File.Exists(filePath))
        {
            return true;
        }

        try
        {
            using var workbook = new XLWorkbook(filePath);
            if (!ConfigSheetMatchesLine(workbook, lineName))
            {
                return true;
            }

            if (!workbook.Worksheets.TryGetWorksheet(TagsSheetName, out var tagsSheet))
            {
                return true;
            }

            var tagRowCount = Math.Max(0, (tagsSheet.LastRowUsed()?.RowNumber() ?? 1) - 1);
            if (tagRowCount > 0)
            {
                return false;
            }

            return LineCatalog.Resolve(lineName).Tags.Count == 0;
        }
        catch
        {
            return true;
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

    static void WriteMqttEndpointsSheet(XLWorkbook workbook, AppSettings settings)
    {
        var sheet = workbook.Worksheets.Add(MqttEndpointsSheetName);
        sheet.Cell(1, 1).Value = "名称";
        sheet.Cell(1, 2).Value = "启用";
        sheet.Cell(1, 3).Value = "Broker";
        sheet.Cell(1, 4).Value = "端口";
        sheet.Cell(1, 5).Value = "ClientId";
        sheet.Cell(1, 6).Value = "用户名";
        sheet.Cell(1, 7).Value = "密码";
        sheet.Cell(1, 8).Value = "TLS";
        sheet.Cell(1, 9).Value = "QoS";
        sheet.Cell(1, 10).Value = "主题";
        sheet.Cell(1, 11).Value = "ID";
        sheet.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var endpoint in settings.MqttEndpoints)
        {
            sheet.Cell(row, 1).Value = endpoint.Name;
            sheet.Cell(row, 2).Value = endpoint.Enabled ? "是" : "否";
            sheet.Cell(row, 3).Value = endpoint.Host;
            sheet.Cell(row, 4).Value = endpoint.Port;
            sheet.Cell(row, 5).Value = endpoint.ClientId;
            sheet.Cell(row, 6).Value = endpoint.Username;
            sheet.Cell(row, 7).Value = endpoint.Password;
            sheet.Cell(row, 8).Value = endpoint.UseTls ? "是" : "否";
            sheet.Cell(row, 9).Value = endpoint.Qos;
            sheet.Cell(row, 10).Value = endpoint.Topic;
            sheet.Cell(row, 11).Value = endpoint.Id;
            row++;
        }

        sheet.Columns(1, 11).AdjustToContents();
    }

    static void ApplyMqttEndpointsSheet(AppSettings settings, XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(MqttEndpointsSheetName, out var sheet))
        {
            return;
        }

        var endpoints = new List<MqttEndpoint>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var host = row.Cell(3).GetString().Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }

            endpoints.Add(new MqttEndpoint
            {
                Id = row.Cell(11).GetString().Trim(),
                Name = row.Cell(1).GetString().Trim(),
                Enabled = ParseBoolText(row.Cell(2).GetString(), true),
                Host = host,
                Port = int.TryParse(ReadCellText(row.Cell(4)), out var port) ? port : LineMqttDefaults.Port,
                ClientId = ReadCellText(row.Cell(5)).Trim(),
                Username = MqttCredentialNormalizer.NormalizeUsername(ReadCellText(row.Cell(6))),
                Password = MqttCredentialNormalizer.NormalizePassword(ReadCellText(row.Cell(7))),
                UseTls = ParseBoolText(row.Cell(8).GetString(), false),
                Qos = int.TryParse(row.Cell(9).GetString(), out var qos) ? qos : 0,
                Topic = row.Cell(10).GetString().Trim()
            });
        }

        if (endpoints.Count > 0)
        {
            settings.MqttEndpoints = endpoints;
        }
    }

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
        settings.MqttPayload = profile;
    }

    static Dictionary<string, string> BuildConfigMap(AppSettings settings) => new(StringComparer.Ordinal)
    {
        ["ExcelFormatVersion"] = FormatVersion.ToString(),
        ["LineConfigRevision"] = LineCatalog.Version.ToString(),
        ["LineName"] = settings.LineName,
        ["DeviceId"] = settings.DeviceId,
        ["PlcModel"] = settings.Plc.Model,
        ["PlcProtocol"] = PlcSettingsHelper.FormatProtocol(settings.Plc.Protocol),
        ["PlcHost"] = settings.Plc.Host,
        ["PlcPort"] = settings.Plc.Port.ToString(),
        ["PlcStation"] = settings.Plc.Station.ToString(),
        ["PlcRack"] = settings.Plc.Rack.ToString(),
        ["PlcSlot"] = settings.Plc.Slot.ToString(),
        ["PlcCpuType"] = settings.Plc.CpuType,
        ["PlcTimeoutMs"] = settings.Plc.TimeoutMs.ToString(),
        ["ScanIntervalMs"] = settings.ScanIntervalMs.ToString(),
        ["PublishIntervalMs"] = settings.PublishIntervalMs.ToString(),
        ["TemperaturePublishThresholdC"] = settings.TemperaturePublishThresholdC.ToString("G"),
        ["TemperaturePrecision"] = settings.TemperaturePrecision.ToString(),
        ["UseSimulator"] = settings.UseSimulator ? "是" : "否",
        ["MqttHost"] = MqttEndpointCatalog.GetPrimary(settings).Host,
        ["MqttPort"] = MqttEndpointCatalog.GetPrimary(settings).Port.ToString(),
        ["MqttClientId"] = MqttEndpointCatalog.GetPrimary(settings).ClientId,
        ["MqttUsername"] = MqttEndpointCatalog.GetPrimary(settings).Username,
        ["MqttPassword"] = MqttEndpointCatalog.GetPrimary(settings).Password,
        ["MqttUseTls"] = MqttEndpointCatalog.GetPrimary(settings).UseTls ? "是" : "否",
        ["MqttQos"] = MqttEndpointCatalog.GetPrimary(settings).Qos.ToString(),
        ["MqttTopic"] = MqttEndpointCatalog.GetPrimary(settings).Topic,
        ["SubscribeTopics"] = string.Join(';', settings.SubscribeTopics),
        ["OperationMode"] = FormatOperationMode(settings.OperationMode),
        ["StartWithWindows"] = settings.StartWithWindows ? "是" : "否",
        ["AutoStartAcquisition"] = settings.AutoStartAcquisition ? "是" : "否",
        ["EnableHistoryRecording"] = settings.EnableHistoryRecording ? "是" : "否",
        ["HistoryRetentionDays"] = settings.HistoryRetentionDays.ToString(),
        ["LogExportDirectory"] = settings.LogExportDirectory ?? string.Empty,
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
            (TagDisplayCategory.Temperature, "名称含「温度」或单位 ℃；当前注胶机编号与当前工作温度同组", "橙色分组 + 左侧色条"),
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
        string? expectedLineName = null)
    {
        if (!workbook.Worksheets.TryGetWorksheet(ConfigSheetName, out var sheet))
        {
            throw new InvalidOperationException($"Excel 缺少工作表「{ConfigSheetName}」。");
        }

        var map = ReadKeyValueSheet(sheet);
        var lineName = expectedLineName
                       ?? GetString(map, "产线名称", settings.LineName);

        settings.LineName = lineName;
        settings.AddressCatalogVersion = GetInt(map, "产线配置版本", settings.AddressCatalogVersion);
        settings.DeviceId = GetString(map, "设备编号", settings.DeviceId);
        settings.Plc.Model = GetString(map, "PLC型号", settings.Plc.Model);
        settings.Plc.Protocol = PlcSettingsHelper.ParseProtocol(GetString(map, "PLC协议", string.Empty), settings.Plc.Model);
        if (settings.Plc.Protocol == PlcProtocol.S7 && PlcSettingsHelper.InferProtocolFromModel(settings.Plc.Model) == PlcProtocol.ModbusTcp)
        {
            settings.ConfigLoadWarnings.Add(
                $"PLC 型号为「{settings.Plc.Model}」（信捷 Modbus），但「PLC协议」为西门子 S7；请确认协议是否正确，并确保点表使用 S7 地址。");
        }

        settings.Plc.Host = GetString(map, "PLC_IP", settings.Plc.Host);
        settings.Plc.Port = GetInt(map, "PLC端口", settings.Plc.Port);
        settings.Plc.Station = (byte)GetInt(map, "PLC站号", settings.Plc.Station);
        settings.Plc.Rack = GetInt(map, "PLC机架号", settings.Plc.Rack);
        settings.Plc.Slot = GetInt(map, "PLC槽位", settings.Plc.Slot);
        settings.Plc.CpuType = GetString(map, "PLC_CPU类型", settings.Plc.CpuType);
        settings.Plc.TimeoutMs = GetInt(map, "PLC超时毫秒", settings.Plc.TimeoutMs);
        PlcSettingsHelper.Normalize(settings.Plc);
        settings.ScanIntervalMs = GetInt(map, "扫描周期毫秒", settings.ScanIntervalMs);
        settings.PublishIntervalMs = GetInt(map, "发布周期毫秒", settings.PublishIntervalMs);
        settings.TemperaturePublishThresholdC = GetDouble(map, "温度发布阈值", settings.TemperaturePublishThresholdC);
        settings.TemperaturePrecision = GetInt(map, "精度", settings.TemperaturePrecision);
        settings.UseSimulator = GetBool(map, "使用模拟数据", settings.UseSimulator);
        settings.Mqtt.Host = GetString(map, "MQTT_Broker", settings.Mqtt.Host);
        settings.Mqtt.Port = GetInt(map, "MQTT端口", settings.Mqtt.Port);
        settings.Mqtt.ClientId = GetString(map, "MQTT_ClientId", settings.Mqtt.ClientId);
        settings.Mqtt.Username = GetString(map, "MQTT_用户名", settings.Mqtt.Username);
        settings.Mqtt.Password = GetString(map, "MQTT_密码", settings.Mqtt.Password);
        settings.Mqtt.UseTls = GetBool(map, "MQTT_TLS", settings.Mqtt.UseTls);
        settings.Mqtt.Qos = GetInt(map, "MQTT_QoS", settings.Mqtt.Qos);
        settings.Mqtt.Topic = GetString(map, "MQTT发布主题", settings.Mqtt.Topic);

        settings.OperationMode = ParseOperationMode(GetString(map, "运行模式", FormatOperationMode(settings.OperationMode)));
        settings.StartWithWindows = GetBool(map, "开机自动启动", settings.StartWithWindows);
        settings.AutoStartAcquisition = GetBool(map, "启动后自动运行", settings.AutoStartAcquisition);
        settings.EnableHistoryRecording = GetBool(map, "记录历史数据", settings.EnableHistoryRecording);
        settings.HistoryRetentionDays = GetInt(map, "历史保留天数", settings.HistoryRetentionDays);
        settings.LogExportDirectory = GetString(map, "日志打包目录", settings.LogExportDirectory);

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
        settings.LogExportDirectory = preserveFrom.LogExportDirectory;
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

    static List<PlcTag> ReadTagsSheet(XLWorkbook workbook, PlcProtocol protocol, List<string> loadWarnings)
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
                if (!PlcAddressMapper.TryApplyTo(tag, protocol, out var addressError))
                {
                    tag.Enabled = false;
                    loadWarnings.Add($"点位「{tag.Name}」地址「{tag.XinjeAddress}」已禁用：{addressError}");
                }
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

    static string ReadCellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        if (cell.TryGetValue(out string text))
        {
            return text;
        }

        if (cell.TryGetValue(out double number))
        {
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return cell.GetString();
    }
}
