using System.Diagnostics;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Diagnostics;

public static class MonitorCoreTests
{
    public static IReadOnlyList<DiagnosticResult> RunAll() =>
    [
        Run("MQTT 主题匹配 +", TestTopicPlusMatch),
        Run("MQTT 主题匹配 #", TestTopicHashMatch),
        Run("订阅主题去重", TestSubscribeTopicNormalize),
        Run("点位显示顺序", TestTagDisplayOrder),
        Run("当前工作温度订阅映射", TestCurrentInjectionRemoteTags),
        Run("平台字段跨产线映射", TestPlatformFieldMappingWithoutCatalogTag),
        Run("华迪订阅字段映射", TestHuadiSubscribeFieldMapping),
        Run("信捷地址解析 D6000", TestXinjeAddress),
        Run("西门子 S7 地址", TestS7Address),
        Run("Float32 字节序", TestRegisterConverter),
        Run("数值显示精度", TestValueFormatting),
        Run("产线点位数量", TestLineCatalog),
        Run("设置读写", TestSettingsRoundTrip),
        Run("Excel 配置读写", TestLineExcelRoundTrip),
        Run("Excel 缺发布周期", TestLegacyExcelMissingPublishInterval),
        Run("Excel 多 MQTT 目标", TestMqttEndpointsExcelRoundTrip),
        Run("Excel 导出保留 MQTT 凭证", TestExportPreservesMqttEndpointCredentials),
        Run("Excel 维护保留点表", TestLineFileMaintenancePreservesCustomTags),
        Run("Excel 字段映射补全", TestPatchEmptyMqttFieldMappings),
        Run("Excel 加载不改写", TestLineExcelLoadPreservesContent),
        Run("废弃热熔胶机点位清理", TestDeprecatedGlueMachineTagsRemoved),
        Run("MQTT 报文映射", TestMqttPayloadMapping),
        Run("properties 上报格式", TestPropertiesPayloadFormat),
        Run("订阅 properties 解析", TestSubscribePropertiesParse),
        Run("MQTT 主题设备号", TestMqttTopicDeviceId),
        Run("订阅设备号解析", TestSubscribeDeviceIdResolver),
        Run("产线 MQTT 默认", TestLineMqttDefaults),
        Run("字段映射参考", TestReferenceFieldMapping),
        Run("点位显示分类", TestTagDisplayCategory),
        Run("历史数据存储", TestHistoryStoreRoundTrip),
        Run("订阅历史点位名称", TestSubscribeHistoryTagNames),
        Run("设置启动不覆盖", TestSettingsSurviveStartupLoad),
        Run("点位稳定 Id", TestStableTagIdentity),
    ];

    static DiagnosticResult Run(string name, Action test)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            test();
            stopwatch.Stop();
            return new DiagnosticResult
            {
                Name = name,
                Passed = true,
                Message = "通过",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                Name = name,
                Passed = false,
                Message = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    static void TestTopicPlusMatch()
    {
        AssertTrue(MqttTopicMatcher.IsMatch("monitor/line-a/telemetry", "monitor/+/telemetry"));
        AssertFalse(MqttTopicMatcher.IsMatch("monitor/line-a/status", "monitor/+/telemetry"));
    }

    static void TestTopicHashMatch()
    {
        AssertTrue(MqttTopicMatcher.IsMatch("monitor/line-a/telemetry/extra", "monitor/#"));
        AssertFalse(MqttTopicMatcher.IsMatch("other/line-a/telemetry", "monitor/#"));
    }

    static void TestSubscribeTopicNormalize()
    {
        var topics = SubscribeTopicHelper.NormalizeTopics([" monitor/a ", "MONITOR/A", ""]);
        AssertTrue(topics.Count == 1);
        AssertTrue(topics[0] == "monitor/a");
    }

    static void TestTagDisplayOrder()
    {
        var catalog = new List<PlcTag>
        {
            new() { Name = "车速", Enabled = true },
            new() { Name = "油温机温度", Enabled = true },
            new() { Name = "门幅", Enabled = true }
        };
        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["门幅"] = 1200,
            ["车速"] = 45.2,
            ["油温机温度"] = 180.5
        };

        var ordered = TagDisplayOrder.OrderRemoteTags(remote, catalog).Select(entry => entry.Name).ToList();
        AssertTrue(ordered.SequenceEqual(["车速", "油温机温度", "门幅"]));

        var profile = new MqttPayloadProfile();
        var catalogWithMqtt = new List<PlcTag>
        {
            new() { Name = "车速", MqttField = "speed", Enabled = true },
            new() { Name = "门幅", Enabled = true }
        };
        var remoteMapped = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["speed"] = 45.2,
            ["门幅"] = 1200
        };
        var orderedMapped = TagDisplayOrder.OrderRemoteTags(remoteMapped, catalogWithMqtt, profile)
            .Select(entry => entry.Name)
            .ToList();
        AssertTrue(orderedMapped.SequenceEqual(["车速", "门幅"]));
    }

    static void TestCurrentInjectionRemoteTags()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
        var catalog = settings.Tags;
        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rrjwd"] = 0,
            ["jgwd"] = 0,
            ["jqwd"] = 0,
            ["zjjbh"] = 2,
            ["speed"] = 45.0
        };

        var ordered = TagDisplayOrder.OrderRemoteTags(remote, catalog, settings.MqttPayload)
            .Select(entry => entry.Name)
            .ToList();
        AssertTrue(ordered.Contains("当前工作胶盘温度"));
        AssertTrue(ordered.Contains("当前工作胶管温度"));
        AssertTrue(ordered.Contains("当前工作胶枪温度"));
        AssertTrue(ordered.Contains("当前注胶机编号"));

        var tray = TagDisplayOrder.OrderRemoteTags(remote, catalog, settings.MqttPayload)
            .First(entry => entry.Name == "当前工作胶盘温度");
        AssertTrue(tray.Value is int zero && zero == 0);
        AssertTrue(tray.CatalogTag?.DisplayCategory == TagDisplayCategory.Temperature);

        var remoteByFieldOnly = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rrjwd"] = 88.5,
            ["jgwd"] = 87.0,
            ["jqwd"] = 86.5
        };
        var catalogMissingMqttField = catalog.Select(tag => new PlcTag
        {
            Name = tag.Name,
            Enabled = tag.Enabled,
            Unit = tag.Unit,
            DataType = tag.DataType,
            DisplayCategory = tag.DisplayCategory,
            MqttField = string.Empty
        }).ToList();
        var linked = TagDisplayOrder.OrderRemoteTags(remoteByFieldOnly, catalogMissingMqttField, settings.MqttPayload)
            .First(entry => entry.Name == "当前工作胶盘温度");
        AssertTrue(linked.CatalogTag is not null);
        AssertTrue(TagDisplayCategoryHelper.Resolve(linked.CatalogTag!) == TagDisplayCategory.Temperature);
    }

    static void TestPlatformFieldMappingWithoutCatalogTag()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
        var profile = settings.MqttPayload ?? MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();
        AssertTrue(!settings.Tags.Any(tag => tag.Name is "下展开转速率" or "注胶量"));

        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["xzksl"] = 12.5,
            ["zjl"] = 3.2
        };

        AssertTrue(TagDisplayOrder.TryResolveCatalogTag("xzksl", settings.Tags, profile, out var expandTag));
        AssertTrue(expandTag.Name == "下展开转速率");
        AssertTrue(TagDisplayOrder.TryResolveCatalogTag("zjl", settings.Tags, profile, out var injectionTag));
        AssertTrue(injectionTag.Name == "注胶量");

        var ordered = TagDisplayOrder.OrderRemoteTags(remote, settings.Tags, profile)
            .Select(entry => entry.Name)
            .ToList();
        AssertTrue(ordered.Contains("下展开转速率"));
        AssertTrue(ordered.Contains("注胶量"));
        AssertTrue(!ordered.Contains("xzksl"));
        AssertTrue(!ordered.Contains("zjl"));

        var snapshots = HistoryTagNameResolver.CreateSubscribeSnapshots(
            remote,
            settings.Tags,
            profile,
            "Good",
            DateTimeOffset.Now);
        AssertTrue(snapshots.Any(snapshot => snapshot.Name == "下展开转速率"));
        AssertTrue(snapshots.Any(snapshot => snapshot.Name == "注胶量"));
    }

    static void TestHuadiSubscribeFieldMapping()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Huadi.Name);
        var profile = settings.MqttPayload ?? MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();
        var expandTag = settings.Tags.First(tag => tag.Name == "下展开转速率");
        var injectionTag = settings.Tags.First(tag => tag.Name == "注胶量");
        AssertTrue(expandTag.MqttField == "xzksl");
        AssertTrue(injectionTag.MqttField == "zjl");

        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["xzksl"] = 12.5,
            ["zjl"] = 3.2
        };
        var ordered = TagDisplayOrder.OrderRemoteTags(remote, settings.Tags, profile, includeDisabledCatalogTags: true)
            .Select(entry => entry.Name)
            .ToList();
        AssertTrue(ordered.Contains("下展开转速率"));
        AssertTrue(ordered.Contains("注胶量"));

        expandTag.Enabled = false;
        injectionTag.Enabled = false;
        ordered = TagDisplayOrder.OrderRemoteTags(remote, settings.Tags, profile, includeDisabledCatalogTags: true)
            .Select(entry => entry.Name)
            .ToList();
        AssertTrue(ordered.Contains("下展开转速率"));
        AssertTrue(ordered.Contains("注胶量"));

        ordered = TagDisplayOrder.OrderRemoteTags(remote, settings.Tags, profile, includeDisabledCatalogTags: false)
            .Select(entry => entry.Name)
            .ToList();
        AssertTrue(!ordered.Contains("下展开转速率"));
        AssertTrue(!ordered.Contains("注胶量"));
    }

    static void TestMqttPayloadMapping()
    {
        var settings = new AppSettings
        {
            DeviceId = "line-1",
            Plc = { Host = "192.168.1.10" },
            MqttPayload = new MqttPayloadProfile
            {
                TagsPath = "tags",
                DeviceIdPath = "deviceId"
            },
            Tags =
            [
                new PlcTag { Name = "车速", MqttField = "speed", Enabled = true },
                new PlcTag { Name = "门幅", Enabled = true }
            ]
        };

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["车速"] = 45.2,
            ["门幅"] = 1200
        };

        var payload = MqttPayloadMapper.BuildPayload(settings, values, true);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        AssertTrue(root.GetProperty("deviceId").GetString() == "line-1");
        AssertTrue(root.GetProperty("tags").GetProperty("speed").GetDouble() == 45.2);

        settings.MqttPayload = MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();
        MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, LineCatalog.Xianhe.Name);
        var propertiesPayload = MqttPayloadMapper.BuildPayload(settings, values, true);
        using var propertiesDoc = JsonDocument.Parse(propertiesPayload);
        var properties = propertiesDoc.RootElement.GetProperty("properties");
        AssertTrue(properties.GetProperty("speed").GetDouble() == 45.2);
        AssertFalse(propertiesDoc.RootElement.TryGetProperty("tags", out _));
        AssertFalse(propertiesDoc.RootElement.TryGetProperty("deviceId", out _));

        var parsed = MqttPayloadMapper.Parse(propertiesDoc.RootElement, settings.MqttPayload);
        AssertTrue(parsed.Tags.ContainsKey("speed"));
    }

    static void TestPropertiesPayloadFormat()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["运行状态"] = 1,
            ["油温机温度"] = 95.2,
            ["车速"] = 45.2
        };

        var payload = MqttPayloadMapper.BuildPayload(settings, values, true);
        using var doc = JsonDocument.Parse(payload);
        var properties = doc.RootElement.GetProperty("properties");
        AssertTrue(properties.GetProperty("run_status").GetString() == "开");
        AssertTrue(Math.Abs(properties.GetProperty("ywjwd").GetDouble() - 95.2) < 0.001);
        AssertTrue(Math.Abs(properties.GetProperty("speed").GetDouble() - 45.2) < 0.001);
        AssertTrue(doc.RootElement.EnumerateObject().Count() == 1);
    }

    static void TestSubscribePropertiesParse()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
        var payload = """
                      {
                        "properties": {
                          "run_status": "开",
                          "speed": 45.2,
                          "ywjwd": 95.2
                        }
                      }
                      """;
        using var doc = JsonDocument.Parse(payload);
        var parsed = MqttPayloadMapper.Parse(doc.RootElement, settings.MqttPayload);
        AssertTrue(parsed.Tags.Count == 3);
        AssertTrue(parsed.Tags["run_status"]?.ToString() == "开");
        AssertTrue(MqttTopicDeviceId.Extract("/RRJFHJ/XHRRJFHJ/properties/report") == "XHRRJFHJ");
    }

    static void TestMqttTopicDeviceId()
    {
        AssertTrue(MqttTopicDeviceId.Extract("/RRJFHJ/HDRRJFHJ/properties/report") == "HDRRJFHJ");
        AssertTrue(MqttTopicDeviceId.Extract("monitor/test-line/telemetry") == "test-line");
    }

    static void TestSubscribeDeviceIdResolver()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Huadi.Name);
        using var doc = JsonDocument.Parse("""
            {
              "deviceId": "华迪热熔胶复合机",
              "properties": { "speed": 1 }
            }
            """);
        var profile = settings.MqttPayload;
        var parsed = MqttPayloadMapper.Parse(doc.RootElement, profile);

        var huadiTopic = "/RRJFHJ/HDRRJFHJ/properties/report";
        var xianheTopic = "/RRJFHJ/XHRRJFHJ/properties/report";
        AssertTrue(SubscribeDeviceIdResolver.Resolve(huadiTopic, doc.RootElement, profile, parsed) == "华迪热熔胶复合机");
        AssertTrue(SubscribeDeviceIdResolver.Resolve(xianheTopic, doc.RootElement, profile, parsed) == "华迪热熔胶复合机");

        using var noPayloadDeviceId = JsonDocument.Parse("""
            {
              "properties": { "speed": 1 }
            }
            """);
        var parsedNoId = MqttPayloadMapper.Parse(noPayloadDeviceId.RootElement, profile);
        AssertTrue(SubscribeDeviceIdResolver.Resolve(huadiTopic, noPayloadDeviceId.RootElement, profile, parsedNoId) == "华迪热熔胶复合机");
        AssertTrue(SubscribeDeviceIdResolver.Resolve(xianheTopic, noPayloadDeviceId.RootElement, profile, parsedNoId) == "先河热熔胶复合机");
        AssertTrue(LineMqttDefaults.ResolveDeviceIdFromTopic(huadiTopic) == "华迪热熔胶复合机");
        AssertTrue(LineMqttDefaults.ResolveDeviceDisplayName("华迪热熔胶复合机") == "华迪热熔胶复合机");
    }

    static void TestLineMqttDefaults()
    {
        var xianhe = new AppSettings();
        LineCatalog.Apply(xianhe, LineCatalog.Xianhe.Name);
        AssertTrue(xianhe.Mqtt.Host == LineMqttDefaults.Host);
        AssertTrue(xianhe.Mqtt.Port == LineMqttDefaults.Port);
        AssertTrue(xianhe.Mqtt.Username == LineMqttDefaults.Username);
        AssertTrue(xianhe.Mqtt.Password == LineMqttDefaults.Password);
        AssertTrue(xianhe.Mqtt.Topic == LineMqttDefaults.XianhePublishTopic);
        AssertTrue(xianhe.Mqtt.ClientId == LineMqttDefaults.XianheClientId);

        var huadi = new AppSettings();
        LineCatalog.Apply(huadi, LineCatalog.Huadi.Name);
        AssertTrue(huadi.Mqtt.Topic == LineMqttDefaults.HuadiPublishTopic);
        AssertTrue(huadi.Mqtt.ClientId == LineMqttDefaults.HuadiClientId);
        AssertTrue(huadi.SubscribeTopics.Count == LineMqttDefaults.SubscribeTopics.Count);

        var customClient = new AppSettings
        {
            LineName = LineCatalog.Huadi.Name,
            Mqtt = { ClientId = "MY-DEVICE-01" }
        };
        AssertTrue(customClient.Mqtt.ClientId == "MY-DEVICE-01");
        AssertTrue(LineMqttDefaults.ResolveClientId(customClient.Mqtt, customClient.LineName) == "MY-DEVICE-01");
        AssertTrue(LineMqttDefaults.ResolveClientIdForLine(LineCatalog.Huadi.Name) == LineMqttDefaults.HuadiClientId);

        var configPath = Path.Combine(Path.GetTempPath(), $"huaguang-config-{Guid.NewGuid():N}.xlsx");
        var templatePath = Path.Combine(Path.GetTempPath(), $"huaguang-template-{Guid.NewGuid():N}.xlsx");
        try
        {
            LineExcelConfigService.Export(LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name), templatePath);
            File.Copy(templatePath, configPath);

            var stale = new AppSettings { LineName = LineCatalog.Xianhe.Name, OperationMode = AppOperationMode.Subscribe };
            var huadiTemplate = LineExcelConfigService.CreateSeedSettings(LineCatalog.Huadi.Name);
            var huadiTemplatePath = Path.Combine(Path.GetTempPath(), $"huaguang-huadi-template-{Guid.NewGuid():N}.xlsx");
            LineExcelConfigService.Export(huadiTemplate, huadiTemplatePath);

            var loadedHuadi = LineExcelConfigService.SwitchLine(
                LineCatalog.Huadi.Name,
                configPath,
                huadiTemplatePath,
                stale);
            AssertTrue(loadedHuadi.LineName == LineCatalog.Huadi.Name);
            AssertTrue(loadedHuadi.Plc.Host == LineCatalog.Huadi.Host);
            AssertTrue(loadedHuadi.Mqtt.ClientId == LineMqttDefaults.HuadiClientId);
            AssertTrue(loadedHuadi.Tags.Count == huadiTemplate.Tags.Count);
            AssertTrue(loadedHuadi.OperationMode == AppOperationMode.Subscribe);

            if (File.Exists(huadiTemplatePath))
            {
                File.Delete(huadiTemplatePath);
            }
        }
        finally
        {
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            if (File.Exists(templatePath))
            {
                File.Delete(templatePath);
            }
        }
    }

    static void TestReferenceFieldMapping()
    {
        var referencePath = MqttFieldMappingCatalog.ResolveReferencePath(FindRepoRootForTests());
        if (referencePath is null)
        {
            return;
        }

        var linePath = Path.Combine(Path.GetTempPath(), $"huaguang-line-{Guid.NewGuid():N}.xlsx");
        try
        {
            LineExcelConfigService.Export(
                LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name),
                linePath);
            LineExcelConfigService.ImportToLineFile(referencePath, linePath, LineCatalog.Xianhe.Name);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, linePath);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "车速").MqttField == "speed");
            AssertTrue(loaded.Tags.First(tag => tag.Name == "运行状态").MqttField == "run_status");
            AssertTrue(string.IsNullOrEmpty(loaded.MqttPayload.TagsPath) ||
                       loaded.MqttPayload.TagsPath == "properties");
        }
        finally
        {
            if (File.Exists(linePath))
            {
                File.Delete(linePath);
            }
        }
    }

    static string? FindRepoRootForTests()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "config", MqttFieldMappingCatalog.ReferenceFileName)))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        var cwd = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(cwd, "config", MqttFieldMappingCatalog.ReferenceFileName)))
        {
            return cwd;
        }

        var sibling = Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", "..", ".."));
        return File.Exists(Path.Combine(sibling, "config", MqttFieldMappingCatalog.ReferenceFileName))
            ? sibling
            : null;
    }

    static void TestXinjeAddress()
    {
        AssertTrue(XinjeXd5eMapper.TryResolve("D6000", out var resolved, out _));
        AssertTrue(resolved.Table == ModbusTable.HoldingRegister);
    }

    static void TestS7Address()
    {
        AssertTrue(SiemensS7AddressMapper.TryResolve("DB1.DBD0", TagDataType.Float32, out var dbReal, out _));
        AssertTrue(dbReal.Area == S7MemoryArea.DataBlock);
        AssertTrue(dbReal.DbNumber == 1);
        AssertTrue(dbReal.ByteOffset == 0);

        AssertTrue(SiemensS7AddressMapper.TryResolve("DB2.DBX4.3", TagDataType.Bool, out var dbBit, out _));
        AssertTrue(dbBit.IsBit);
        AssertTrue(dbBit.BitOffset == 3);

        AssertTrue(SiemensS7AddressMapper.TryResolve("MW10", TagDataType.Int16, out var memoryWord, out _));
        AssertTrue(memoryWord.Area == S7MemoryArea.Memory);
        AssertTrue(memoryWord.ByteOffset == 10);

        AssertTrue(SiemensS7AddressMapper.TryResolve("I0.0", TagDataType.Bool, out var inputBit, out _));
        AssertTrue(inputBit.Area == S7MemoryArea.Input);

        AssertTrue(SiemensS7AddressMapper.TryResolve("IW64", TagDataType.Int16, out var inputWord, out _));
        AssertTrue(inputWord.Area == S7MemoryArea.Input);
        AssertTrue(inputWord.ByteOffset == 64);
        AssertTrue(inputWord.Normalized == "IW64");

        AssertTrue(SiemensS7AddressMapper.TryResolve("%IW128", TagDataType.UInt16, out var percentWord, out _));
        AssertTrue(percentWord.ByteOffset == 128);

        AssertTrue(SiemensS7AddressMapper.TryResolve("EW64", TagDataType.Int16, out var germanWord, out _));
        AssertTrue(germanWord.Normalized == "IW64");

        AssertTrue(SiemensS7AddressMapper.TryResolve("ID100", TagDataType.Float32, out var inputDword, out _));
        AssertTrue(inputDword.ByteOffset == 100);
    }

    static void TestRegisterConverter()
    {
        var value = RegisterConverter.ToValue([0x0000, 0x0000], TagDataType.Float32, ByteOrder.CDAB);
        AssertTrue(value is float);
        AssertTrue(Math.Abs(Convert.ToSingle(value)) < 0.001);
    }

    static void TestValueFormatting()
    {
        var tag = new PlcTag { Name = "车速", DataType = TagDataType.Float32, DisplayPrecision = 2 };
        AssertTrue(ValueFormatting.FormatDisplay(tag, 45.234, 1) == "45.23");
    }

    static void TestLineCatalog()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
        AssertTrue(settings.Tags.Count >= 16);
        AssertTrue(settings.Tags.Any(tag => tag.Name.Contains("温度", StringComparison.Ordinal)));
        var productSku = settings.Tags.FirstOrDefault(tag => tag.Name == "产品货号");
        AssertTrue(productSku is not null);
        AssertTrue(productSku!.IsManual);
        AssertTrue(productSku.DataType == TagDataType.String);
        AssertTrue(productSku.MqttField == "cphh");
    }

    static void TestSettingsRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-test-{Guid.NewGuid():N}.json");
        try
        {
            var original = new AppSettings();
            LineCatalog.Apply(original, LineCatalog.Xianhe.Name);
            original.DeviceId = "TEST-DEVICE";
            var json = JsonSerializer.Serialize(original);
            File.WriteAllText(tempPath, json);

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(tempPath));
            AssertTrue(loaded is not null);
            AssertTrue(loaded!.DeviceId == "TEST-DEVICE");
            AssertTrue(loaded.Tags.Count == original.Tags.Count);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestLineExcelRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-{Guid.NewGuid():N}.xlsx");
        try
        {
            var original = new AppSettings();
            LineCatalog.Apply(original, LineCatalog.Xianhe.Name);
            original.Plc.Host = "192.168.6.99";
            original.ScanIntervalMs = 5000;
            original.PublishIntervalMs = 120_000;
            original.MqttPayload.TagsPath = "data.tags";
            original.Tags.First(tag => tag.Name == "车速").MqttField = "speed";
            original.Tags.First(tag => tag.Name == "运行状态").DisplayCategory = TagDisplayCategory.Switch;
            original.Tags.First(tag => tag.Name == "车速").DisplayCategory = TagDisplayCategory.Process;
            original.SubscribeTopics = ["monitor/+/telemetry", "monitor/test/#"];
            LineExcelConfigService.Export(original, tempPath);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(loaded.LineName == original.LineName);
            AssertTrue(loaded.Plc.Host == "192.168.6.99");
            AssertTrue(loaded.ScanIntervalMs == 5000);
            AssertTrue(loaded.PublishIntervalMs == 120_000);
            AssertTrue(loaded.MqttPayload.TagsPath == "data.tags");
            AssertTrue(loaded.Tags.Count == original.Tags.Count);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "车速").MqttField == "speed");
            AssertTrue(loaded.Tags.First(tag => tag.Name == "运行状态").DataType == TagDataType.Int16);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "运行状态").DisplayCategory == TagDisplayCategory.Switch);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "车速").DisplayCategory == TagDisplayCategory.Process);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "油温机温度").DisplayCategory == TagDisplayCategory.Temperature);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "胶辊型号").DisplayCategory == TagDisplayCategory.Setting);
            AssertTrue(loaded.Tags.Any(tag => tag.Name == "产品货号"));
            AssertTrue(loaded.SubscribeTopics.Count == 2);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestLegacyExcelMissingPublishInterval()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-pub-{Guid.NewGuid():N}.xlsx");
        try
        {
            var original = LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name);
            original.ScanIntervalMs = 2000;
            original.PublishIntervalMs = 60_000;
            LineExcelConfigService.Export(original, tempPath);

            using (var workbook = new XLWorkbook(tempPath))
            {
                var configSheet = workbook.Worksheet(LineExcelConfigService.ConfigSheetName);
                foreach (var row in configSheet.RowsUsed().ToList())
                {
                    if (row.Cell(1).GetString().Trim() == "发布周期毫秒")
                    {
                        row.Delete();
                        break;
                    }
                }

                workbook.SaveAs(tempPath);
            }

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(loaded.ScanIntervalMs == 2000);
            AssertTrue(loaded.PublishIntervalMs == 60_000);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestMqttEndpointsExcelRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-mqtt-endpoints-{Guid.NewGuid():N}.xlsx");
        try
        {
            var original = LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name);
            original.MqttEndpoints =
            [
                MqttEndpoint.FromSettings(original.Mqtt, "平台 A"),
                new MqttEndpoint
                {
                    Name = "平台 B",
                    Host = "10.0.0.8",
                    Port = 1883,
                    ClientId = "BACKUP",
                    Username = "user2",
                    Password = "pass2",
                    Topic = "/backup/topic"
                }
            ];
            LineExcelConfigService.Export(original, tempPath);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(loaded.MqttEndpoints.Count == 2);
            AssertTrue(loaded.MqttEndpoints[1].Host == "10.0.0.8");
            AssertTrue(loaded.MqttEndpoints[1].ClientId == "BACKUP");
            AssertTrue(loaded.MqttEndpoints[1].Username == "user2");
            AssertTrue(loaded.MqttEndpoints[1].Password == "pass2");
            MqttEndpointCatalog.Normalize(loaded);
            AssertTrue(loaded.Mqtt.Host == loaded.MqttEndpoints[0].Host);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestExportPreservesMqttEndpointCredentials()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-mqtt-creds-{Guid.NewGuid():N}.xlsx");
        try
        {
            var settings = LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name);
            settings.Mqtt.Username = "legacy-user";
            settings.Mqtt.Password = "legacy-pass";
            settings.MqttEndpoints =
            [
                new MqttEndpoint
                {
                    Name = "平台",
                    Host = settings.Mqtt.Host,
                    Port = settings.Mqtt.Port,
                    ClientId = settings.Mqtt.ClientId,
                    Username = "endpoint-user",
                    Password = "endpoint-pass",
                    Topic = settings.Mqtt.Topic
                }
            ];

            LineExcelConfigService.Export(settings, tempPath);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(loaded.MqttEndpoints[0].Username == "endpoint-user");
            AssertTrue(loaded.MqttEndpoints[0].Password == "endpoint-pass");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestLineFileMaintenancePreservesCustomTags()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-maint-{Guid.NewGuid():N}.xlsx");
        try
        {
            var settings = LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name);
            var workTag = settings.Tags.First(tag => tag.Name == "当前工作胶盘温度");
            workTag.Source = TagSource.Plc;
            workTag.ManualValue = string.Empty;
            workTag.XinjeAddress = "D9999";
            workTag.DataType = TagDataType.Float32;
            LineExcelConfigService.Export(settings, tempPath);

            using (var workbook = new XLWorkbook(tempPath))
            {
                var configSheet = workbook.Worksheet(LineExcelConfigService.ConfigSheetName);
                foreach (var row in configSheet.RowsUsed())
                {
                    if (row.Cell(1).GetString().Trim() == "产线配置版本")
                    {
                        row.Cell(2).Value = (LineCatalog.Version - 1).ToString();
                        break;
                    }
                }

                workbook.SaveAs(tempPath);
            }

            LineExcelConfigService.ApplyLineFileMaintenance(tempPath);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            var restored = loaded.Tags.First(tag => tag.Name == "当前工作胶盘温度");
            AssertTrue(restored.Source == TagSource.Plc);
            AssertTrue(restored.XinjeAddress == "D9999");
            AssertTrue(LineExcelConfigService.ReadLineConfigRevision(tempPath) == LineCatalog.Version);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestPatchEmptyMqttFieldMappings()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-map-{Guid.NewGuid():N}.xlsx");
        try
        {
            var settings = LineExcelConfigService.CreateSeedSettings(LineCatalog.Xianhe.Name);
            LineExcelConfigService.Export(settings, tempPath);

            using (var workbook = new XLWorkbook(tempPath))
            {
                var mappingSheet = workbook.Worksheet(LineExcelConfigService.FieldMappingSheetName);
                foreach (var name in new[] { "当前工作胶盘温度", "胶辊型号", "门幅" })
                {
                    var row = mappingSheet.RowsUsed()
                        .First(r => r.Cell(2).GetString().Trim() == name);
                    row.Cell(1).Clear();
                }

                workbook.SaveAs(tempPath);
            }

            AssertTrue(LineExcelConfigService.PatchEmptyMqttFieldMappings(tempPath));

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "当前工作胶盘温度").MqttField == "rrjwd");
            AssertTrue(loaded.Tags.First(tag => tag.Name == "胶辊型号").MqttField == "jgxh");
            AssertTrue(loaded.Tags.First(tag => tag.Name == "门幅").MqttField == "mf");

            using var verifyWorkbook = new XLWorkbook(tempPath);
            var verifySheet = verifyWorkbook.Worksheet(LineExcelConfigService.FieldMappingSheetName);
            var verifyRow = verifySheet.RowsUsed()
                .First(r => r.Cell(2).GetString().Trim() == "当前工作胶盘温度");
            AssertTrue(verifyRow.Cell(1).GetString().Trim() == "rrjwd");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestDeprecatedGlueMachineTagsRemoved()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-deprecated-{Guid.NewGuid():N}.xlsx");
        try
        {
            var settings = new AppSettings();
            LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
            settings.Tags.Add(new PlcTag
            {
                Name = "热溶胶盘温度（热熔胶机1）",
                XinjeAddress = "D6000",
                DataType = TagDataType.Float32,
                Enabled = true
            });
            XinjeXd5eMapper.ApplyTo(settings.Tags[^1]);
            LineExcelConfigService.Export(settings, tempPath);

            LineExcelConfigService.RemoveDeprecatedTagsFromFile(tempPath);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertFalse(loaded.Tags.Any(tag => DeprecatedLineTags.IsPerMachineGlueTemperature(tag.Name)));
            AssertTrue(loaded.Tags.Any(tag => tag.Name == "当前注胶机编号"));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestLineExcelLoadPreservesContent()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-rev-{Guid.NewGuid():N}.xlsx");
        try
        {
            var legacy = new AppSettings();
            LineCatalog.Apply(legacy, LineCatalog.Xianhe.Name);
            legacy.Tags = legacy.Tags.Where(tag => tag.Name != "产品货号").ToList();
            legacy.Mqtt.Host = "127.0.0.1";
            legacy.Mqtt.Port = 1883;
            legacy.Mqtt.Username = "local-user";
            legacy.Mqtt.Password = "local-pass";
            LineExcelConfigService.Export(legacy, tempPath);

            using (var workbook = new XLWorkbook(tempPath))
            {
                var configSheet = workbook.Worksheet(LineExcelConfigService.ConfigSheetName);
                foreach (var row in configSheet.RowsUsed())
                {
                    if (row.Cell(1).GetString() == "产线配置版本")
                    {
                        row.Cell(2).Value = (LineCatalog.Version - 1).ToString();
                        break;
                    }
                }

                workbook.SaveAs(tempPath);
            }

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(LineExcelConfigService.ReadLineConfigRevision(tempPath) == LineCatalog.Version - 1);
            AssertTrue(loaded.AddressCatalogVersion == LineCatalog.Version - 1);
            AssertFalse(loaded.Tags.Any(tag => tag.Name == "产品货号"));
            AssertTrue(loaded.Mqtt.Host == "127.0.0.1");
            AssertTrue(loaded.Mqtt.Port == 1883);
            AssertTrue(loaded.Mqtt.Username == "local-user");
            AssertTrue(loaded.Mqtt.Password == "local-pass");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void TestTagDisplayCategory()
    {
        var runStatus = new PlcTag { Name = "运行状态", DataType = TagDataType.Int16 };
        var temperature = new PlcTag { Name = "复合温度", Unit = "℃", DataType = TagDataType.Float32 };
        var speed = new PlcTag { Name = "车速", DataType = TagDataType.Float32 };
        var manual = new PlcTag { Name = "胶辊型号", Source = TagSource.Manual, DataType = TagDataType.String };

        AssertTrue(TagDisplayCategoryHelper.InferCategory(runStatus) == TagDisplayCategory.Switch);
        AssertTrue(TagDisplayCategoryHelper.InferCategory(temperature) == TagDisplayCategory.Temperature);
        AssertTrue(TagDisplayCategoryHelper.InferCategory(speed) == TagDisplayCategory.Process);
        AssertTrue(TagDisplayCategoryHelper.InferCategory(manual) == TagDisplayCategory.Setting);
        var currentInjection = new PlcTag { Name = "当前注胶机编号", DataType = TagDataType.Int16 };
        AssertTrue(TagDisplayCategoryHelper.InferCategory(currentInjection) == TagDisplayCategory.Temperature);
        AssertTrue(TagDisplayCategoryHelper.TryParseLabel("开关状态", out var parsed) && parsed == TagDisplayCategory.Switch);
        AssertTrue(TagDisplayCategoryHelper.Resolve(new PlcTag { Name = "自定义", DisplayCategory = TagDisplayCategory.Other }) == TagDisplayCategory.Other);
        var misconfigured = new PlcTag
        {
            Name = "当前工作胶盘温度",
            Unit = "℃",
            DisplayCategory = TagDisplayCategory.Process
        };
        AssertTrue(TagDisplayCategoryHelper.Resolve(misconfigured) == TagDisplayCategory.Temperature);
    }

    static void TestHistoryStoreRoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-{Guid.NewGuid():N}.db");
        try
        {
            var store = new HistoryStore(dbPath);
            store.InitializeAsync().GetAwaiter().GetResult();

            var request = new HistorySampleWriteRequest
            {
                DeviceId = "测试设备",
                OperationMode = AppOperationMode.Acquisition,
                Quality = "Good",
                Tags =
                [
                    new TagSnapshot
                    {
                        TagId = "speed",
                        Name = "车速",
                        Unit = "m/min",
                        Value = 45.2,
                        Quality = "Good",
                        Timestamp = DateTimeOffset.Now
                    },
                    new TagSnapshot
                    {
                        TagId = "run",
                        Name = "运行状态",
                        Value = 1,
                        Quality = "Good",
                        Timestamp = DateTimeOffset.Now
                    }
                ]
            };
            var sampleId = store.AppendAsync(request).GetAwaiter().GetResult();
            AssertTrue(sampleId > 0);

            var rows = store.QueryAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                Limit = 10
            }).GetAwaiter().GetResult();
            AssertTrue(rows.Count == 1);
            AssertTrue(rows[0].TagCount == 2);

            var detail = store.GetDetailAsync(sampleId, 1).GetAwaiter().GetResult();
            AssertTrue(detail is not null);
            AssertTrue(detail!.Tags.Count == 2);
            AssertTrue(detail.Tags.Any(tag => tag.DisplayValue == "开"));

            var table = store.QueryTableAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                Limit = 10
            }, 1, ["车速", "运行状态"]).GetAwaiter().GetResult();
            AssertTrue(table.Rows.Count == 1);
            AssertTrue(table.Columns.Count == 2);
            AssertTrue(table.Rows[0].TagCells[0].Text == "45.2");
            AssertTrue(table.Rows[0].TagCells[1].Text == "开");

            var tableWithPreferredOnly = store.QueryTableAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                Limit = 10
            }, 1, ["车速", "运行状态", "新增点位"], tagUnitHints: new Dictionary<string, string?>
            {
                ["新增点位"] = "bar"
            }).GetAwaiter().GetResult();
            AssertTrue(tableWithPreferredOnly.Columns.Count == 3);
            AssertTrue(tableWithPreferredOnly.Columns.Any(column => column.TagName == "新增点位"));
            AssertTrue(tableWithPreferredOnly.Rows[0].TagCells[2].Text == "—");

            var deviceATags = store.GetDistinctTagNamesAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                DeviceId = "测试设备"
            }).GetAwaiter().GetResult();
            AssertTrue(deviceATags.SequenceEqual(["车速", "运行状态"], StringComparer.Ordinal));

            var secondId = store.AppendAsync(new HistorySampleWriteRequest
            {
                DeviceId = "另一设备",
                OperationMode = request.OperationMode,
                Quality = request.Quality,
                Tags =
                [
                    new TagSnapshot
                    {
                        TagId = "pressure",
                        Name = "压力",
                        Unit = "MPa",
                        Value = 1.2,
                        Quality = "Good",
                        Timestamp = DateTimeOffset.Now
                    }
                ]
            }).GetAwaiter().GetResult();
            AssertTrue(secondId > 0);

            var allTags = store.GetDistinctTagNamesAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1)
            }).GetAwaiter().GetResult();
            AssertTrue(allTags.SequenceEqual(["压力", "车速", "运行状态"], StringComparer.Ordinal));

            var deviceBTags = store.GetDistinctTagNamesAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                DeviceId = "另一设备"
            }).GetAwaiter().GetResult();
            AssertTrue(deviceBTags.SequenceEqual(["压力"], StringComparer.Ordinal));

            var matchingCount = store.CountMatchingAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1)
            }).GetAwaiter().GetResult();
            AssertTrue(matchingCount == 2);

            AssertTrue(store.DeleteSampleAsync(sampleId).GetAwaiter().GetResult());
            AssertTrue(store.GetDetailAsync(sampleId, 1).GetAwaiter().GetResult() is null);

            var deleted = store.DeleteMatchingAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                DeviceId = "另一设备"
            }).GetAwaiter().GetResult();
            AssertTrue(deleted == 1);
            AssertTrue(store.GetStatsAsync().GetAwaiter().GetResult().SampleCount == 0);

            store.AppendAsync(request).GetAwaiter().GetResult();
            AssertTrue(store.DeleteAllAsync().GetAwaiter().GetResult() == 1);
            AssertTrue(store.GetStatsAsync().GetAwaiter().GetResult().SampleCount == 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    static void TestSubscribeHistoryTagNames()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-subscribe-{Guid.NewGuid():N}.db");
        try
        {
            var settings = new AppSettings();
            LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
            var catalogTags = settings.Tags;
            var profile = settings.MqttPayload ?? MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();

            var remoteTags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rrjwd"] = 88.5,
                ["speed"] = 45.0
            };
            var snapshots = HistoryTagNameResolver.CreateSubscribeSnapshots(
                remoteTags,
                catalogTags,
                profile,
                "Good",
                DateTimeOffset.Now);
            AssertTrue(snapshots.Any(snapshot => snapshot.Name == "当前工作胶盘温度"));
            AssertTrue(snapshots.Any(snapshot => snapshot.Name == "车速"));
            AssertTrue(!snapshots.Any(snapshot => snapshot.Name == "rrjwd"));

            var disabledCatalog = settings.Tags.First(tag => tag.Name == "油温机温度");
            disabledCatalog.Enabled = false;
            var withExtra = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rrjwd"] = 88.5,
                ["ywjwd"] = 65.0,
                ["unknown_field"] = 1.2
            };
            var allSnapshots = HistoryTagNameResolver.CreateSubscribeSnapshots(
                withExtra,
                settings.Tags,
                profile,
                "Good",
                DateTimeOffset.Now);
            AssertTrue(allSnapshots.Any(snapshot => snapshot.Name == "当前工作胶盘温度"));
            AssertTrue(allSnapshots.Any(snapshot => snapshot.Name == "油温机温度"));
            AssertTrue(allSnapshots.Any(snapshot => snapshot.Name == "unknown_field"));
            AssertTrue(allSnapshots.Count == 3);

            var store = new HistoryStore(dbPath);
            store.InitializeAsync().GetAwaiter().GetResult();
            store.AppendAsync(new HistorySampleWriteRequest
            {
                DeviceId = "remote-1",
                OperationMode = AppOperationMode.Subscribe,
                Quality = "Good",
                Tags =
                [
                    new TagSnapshot
                    {
                        TagId = "rrjwd",
                        Name = "rrjwd",
                        Value = 88.5,
                        Quality = "Good",
                        Timestamp = DateTimeOffset.Now
                    },
                    new TagSnapshot
                    {
                        TagId = "speed",
                        Name = "speed",
                        Value = 45.0,
                        Quality = "Good",
                        Timestamp = DateTimeOffset.Now
                    }
                ]
            }).GetAwaiter().GetResult();

            var query = new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                Limit = 10
            };
            var distinctTags = store.GetDistinctTagNamesAsync(query, catalogTags, profile).GetAwaiter().GetResult();
            AssertTrue(distinctTags.Contains("当前工作胶盘温度"));
            AssertTrue(distinctTags.Contains("车速"));
            AssertTrue(!distinctTags.Contains("rrjwd"));

            var table = store.QueryTableAsync(
                query,
                settings.TemperaturePrecision,
                ["当前工作胶盘温度", "车速"],
                catalogTags: catalogTags,
                mqttProfile: profile).GetAwaiter().GetResult();
            AssertTrue(table.Rows.Count == 1);
            AssertTrue(table.Rows[0].TagCells[0].Text == "88.50");
            AssertTrue(table.Rows[0].TagCells[1].Text == "45.00");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    static void TestSettingsSurviveStartupLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), $"huaguang-settings-{Guid.NewGuid():N}");
        var configPath = Path.Combine(root, "lines", "产线配置.xlsx");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            var saved = LineExcelConfigService.CreateSeedSettings(LineCatalog.LineNames[0]);
            saved.DeviceId = "USER-DEVICE-99";
            saved.AutoStartAcquisition = false;
            saved.EnableHistoryRecording = false;
            saved.HistoryRetentionDays = 30;
            saved.Plc.Host = "10.0.0.88";
            saved.Mqtt.Host = "10.0.0.99";
            saved.Mqtt.Port = 1888;
            saved.Mqtt.Topic = "/custom/topic";
            saved.Tags.Add(new PlcTag { Name = "测试点", Source = TagSource.Manual, ManualValue = "1" });
            MqttFieldMappingCatalog.ApplyDefaults(saved.Tags, saved.LineName);
            PlcTagIdentity.AssignStableIds(saved);
            LineExcelConfigService.Export(saved, configPath);

            var loaded = LineExcelConfigService.LoadLineExcel(
                saved.LineName,
                configPath,
                templateFilePath: configPath);
            AssertTrue(loaded.DeviceId == "USER-DEVICE-99");
            AssertTrue(loaded.Plc.Host == "10.0.0.88");
            AssertTrue(loaded.Mqtt.Host == "10.0.0.99");
            AssertTrue(loaded.AutoStartAcquisition == false);
            AssertTrue(loaded.HistoryRetentionDays == 30);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    static void TestStableTagIdentity()
    {
        var lineName = LineCatalog.LineNames[0];
        var root = Path.Combine(Path.GetTempPath(), $"huaguang-tagid-{Guid.NewGuid():N}");
        var configPath = Path.Combine(root, "lines", $"{lineName}.xlsx");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var settings = new AppSettings
            {
                LineName = lineName,
                Tags =
                [
                    new PlcTag { Name = "车速", Enabled = true, XinjeAddress = "D100", DataType = TagDataType.Float32 }
                ]
            };
            PlcTagIdentity.AssignStableIds(settings);
            LineExcelConfigService.Export(settings, configPath);

            var first = LineExcelConfigService.LoadLineExcel(lineName, configPath, configPath);
            var second = LineExcelConfigService.LoadLineExcel(lineName, configPath, configPath);
            var expected = PlcTagIdentity.CreateStableId(lineName, "车速");
            AssertTrue(first.Tags[0].Id == expected);
            AssertTrue(second.Tags[0].Id == expected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("断言失败");
        }
    }

    static void AssertFalse(bool condition) => AssertTrue(!condition);
}
