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
        Run("信捷地址解析 D6000", TestXinjeAddress),
        Run("Float32 字节序", TestRegisterConverter),
        Run("数值显示精度", TestValueFormatting),
        Run("产线点位数量", TestLineCatalog),
        Run("设置读写", TestSettingsRoundTrip),
        Run("Excel 配置读写", TestLineExcelRoundTrip),
        Run("Excel 点表局部修补", TestPatchLineExcelRunStatusAndPrecision),
        Run("Excel 维护保留点表", TestLineFileMaintenancePreservesCustomTags),
        Run("Excel 配置补全点位", TestLineExcelRevisionMerge),
        Run("MQTT 报文映射", TestMqttPayloadMapping),
        Run("properties 上报格式", TestPropertiesPayloadFormat),
        Run("产线 MQTT 默认", TestLineMqttDefaults),
        Run("字段映射参考", TestReferenceFieldMapping),
        Run("点位显示分类", TestTagDisplayCategory),
        Run("历史数据存储", TestHistoryStoreRoundTrip),
        Run("设置启动不覆盖", TestSettingsSurviveStartupLoad),
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
            new() { Name = "热溶胶盘温度（热熔胶机1）", Enabled = true },
            new() { Name = "门幅", Enabled = true }
        };
        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["门幅"] = 1200,
            ["车速"] = 45.2,
            ["热溶胶盘温度（热熔胶机1）"] = 180.5
        };

        var ordered = TagDisplayOrder.OrderRemoteTags(remote, catalog).Select(entry => entry.Name).ToList();
        AssertTrue(ordered.SequenceEqual(["车速", "热溶胶盘温度（热熔胶机1）", "门幅"]));

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
            ["热溶胶盘温度（热熔胶机1）"] = 95.2,
            ["胶管温度（热熔胶机1）"] = 150.4
        };

        var payload = MqttPayloadMapper.BuildPayload(settings, values, true);
        using var doc = JsonDocument.Parse(payload);
        var properties = doc.RootElement.GetProperty("properties");
        AssertTrue(properties.GetProperty("run_status").GetInt32() == 1);
        AssertTrue(Math.Abs(properties.GetProperty("rrjwd1").GetDouble() - 95.2) < 0.001);
        AssertTrue(Math.Abs(properties.GetProperty("jgwd1").GetDouble() - 150.4) < 0.001);
        AssertTrue(doc.RootElement.EnumerateObject().Count() == 1);
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
        AssertTrue(huadi.SubscribeTopics.Count == 2);

        var legacy = new AppSettings
        {
            LineName = LineCatalog.Huadi.Name,
            Mqtt = { Host = "127.0.0.1", Port = 1883, Topic = "monitor/{deviceId}/telemetry" },
            SubscribeTopics = ["monitor/+/telemetry"]
        };
        LineMqttDefaults.MigrateLegacySettings(legacy);
        AssertTrue(legacy.Mqtt.Host == LineMqttDefaults.Host);
        AssertTrue(legacy.Mqtt.Topic == LineMqttDefaults.HuadiPublishTopic);
        AssertTrue(legacy.SubscribeTopics[0] == LineMqttDefaults.XianhePublishTopic);

        var hostOnly = new AppSettings
        {
            Mqtt = { Host = LineMqttDefaults.Host, Port = LineMqttDefaults.Port, Username = "", Password = "" }
        };
        LineMqttDefaults.MigrateLegacySettings(hostOnly);
        AssertTrue(hostOnly.Mqtt.Username == LineMqttDefaults.Username);
        AssertTrue(hostOnly.Mqtt.Password == LineMqttDefaults.Password);

        var (username, password) = LineMqttDefaults.ResolveCredentials(hostOnly.Mqtt);
        AssertTrue(username == LineMqttDefaults.Username);
        AssertTrue(password == LineMqttDefaults.Password);

        var legacyClient = new AppSettings
        {
            LineName = LineCatalog.Xianhe.Name,
            DeviceId = LineCatalog.Xianhe.Name,
            Mqtt = { ClientId = LineCatalog.Xianhe.Name }
        };
        LineMqttDefaults.MigrateLegacySettings(legacyClient);
        AssertTrue(legacyClient.Mqtt.ClientId == LineMqttDefaults.XianheClientId);

        var customClient = new AppSettings
        {
            LineName = LineCatalog.Huadi.Name,
            Mqtt = { ClientId = "MY-DEVICE-01" }
        };
        LineMqttDefaults.MigrateLegacySettings(customClient);
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
            LineExcelConfigService.EnsureLineFile(linePath, LineCatalog.Xianhe.Name);
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
            AssertTrue(loaded.MqttPayload.TagsPath == "data.tags");
            AssertTrue(loaded.Tags.Count == original.Tags.Count);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "车速").MqttField == "speed");
            AssertTrue(loaded.Tags.First(tag => tag.Name == "运行状态").DataType == TagDataType.Int16);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "运行状态").DisplayCategory == TagDisplayCategory.Switch);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "车速").DisplayCategory == TagDisplayCategory.Process);
            AssertTrue(loaded.Tags.First(tag => tag.Name == "热溶胶盘温度（热熔胶机1）").DisplayCategory == TagDisplayCategory.Temperature);
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

    static void TestPatchLineExcelRunStatusAndPrecision()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-patch-{Guid.NewGuid():N}.xlsx");
        try
        {
            var settings = new AppSettings();
            LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
            settings.Tags.First(tag => tag.Name == RunStatusFormatting.TagName).DataType = TagDataType.Bool;
            settings.TemperaturePrecision = 1;
            LineExcelConfigService.Export(settings, tempPath);

            using (var workbook = new XLWorkbook(tempPath))
            {
                var configSheet = workbook.Worksheet(LineExcelConfigService.ConfigSheetName);
                foreach (var row in configSheet.RowsUsed())
                {
                    if (row.Cell(1).GetString().Trim() == "精度")
                    {
                        row.Cell(1).Value = "温度精度";
                        row.Cell(2).Value = "1";
                        break;
                    }
                }

                workbook.SaveAs(tempPath);
            }

            AssertTrue(LineExcelConfigService.PatchRunStatusAndPrecision(tempPath));

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            var runStatus = loaded.Tags.First(tag => tag.Name == RunStatusFormatting.TagName);
            AssertTrue(runStatus.DataType == TagDataType.Int16);
            AssertTrue(loaded.TemperaturePrecision == 1);

            using var verifyWorkbook = new XLWorkbook(tempPath);
            var verifyConfig = verifyWorkbook.Worksheet(LineExcelConfigService.ConfigSheetName);
            AssertTrue(verifyConfig.RowsUsed().Any(row => row.Cell(1).GetString().Trim() == "精度"));
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

    static void TestLineExcelRevisionMerge()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-line-rev-{Guid.NewGuid():N}.xlsx");
        try
        {
            var legacy = new AppSettings();
            LineCatalog.Apply(legacy, LineCatalog.Xianhe.Name);
            legacy.Tags = legacy.Tags.Where(tag => tag.Name != "产品货号").ToList();
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

            LineExcelConfigService.EnsureLineFile(tempPath, LineCatalog.Xianhe.Name);

            var loaded = new AppSettings();
            LineExcelConfigService.Apply(loaded, tempPath);
            AssertTrue(LineExcelConfigService.ReadLineConfigRevision(tempPath) == LineCatalog.Version);
            AssertFalse(loaded.Tags.Any(tag => tag.Name == "产品货号"));
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
        AssertTrue(TagDisplayCategoryHelper.InferCategory(new PlcTag { Name = "备注", DataType = TagDataType.String }, true) == TagDisplayCategory.Switch);
        AssertTrue(TagDisplayCategoryHelper.TryParseLabel("开关状态", out var parsed) && parsed == TagDisplayCategory.Switch);
        AssertTrue(TagDisplayCategoryHelper.Resolve(new PlcTag { Name = "自定义", DisplayCategory = TagDisplayCategory.Other }) == TagDisplayCategory.Other);
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
            AssertTrue(detail.Tags.Any(tag => tag.DisplayValue == "运行中"));

            var table = store.QueryTableAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1),
                Limit = 10
            }, 1, ["车速", "运行状态"]).GetAwaiter().GetResult();
            AssertTrue(table.Rows.Count == 1);
            AssertTrue(table.Columns.Count == 2);
            AssertTrue(table.Rows[0].Cells[0] == "45.2");
            AssertTrue(table.Rows[0].Cells[1] == "运行中");

            var matchingCount = store.CountMatchingAsync(new HistoryQuery
            {
                From = DateTimeOffset.Now.AddHours(-1),
                To = DateTimeOffset.Now.AddHours(1)
            }).GetAwaiter().GetResult();
            AssertTrue(matchingCount == 1);

            AssertTrue(store.DeleteSampleAsync(sampleId).GetAwaiter().GetResult());
            AssertTrue(store.GetDetailAsync(sampleId, 1).GetAwaiter().GetResult() is null);

            var secondId = store.AppendAsync(new HistorySampleWriteRequest
            {
                DeviceId = "另一设备",
                OperationMode = request.OperationMode,
                Quality = request.Quality,
                Tags = request.Tags
            }).GetAwaiter().GetResult();
            AssertTrue(secondId > 0);
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

    static void TestSettingsSurviveStartupLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), $"huaguang-settings-{Guid.NewGuid():N}");
        var configPath = Path.Combine(root, "lines", "产线配置.xlsx");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            var saved = new AppSettings
            {
                DeviceId = "USER-DEVICE-99",
                LineName = LineCatalog.LineNames[0],
                AutoStartAcquisition = false,
                EnableHistoryRecording = false,
                HistoryRetentionDays = 30,
                AddressCatalogVersion = LineCatalog.Version,
                Plc = new PlcSettings { Host = "10.0.0.88" },
                Mqtt = new MqttSettings { Host = "10.0.0.99", Port = 1888, Topic = "/custom/topic" },
                Tags = [new PlcTag { Name = "测试点", Source = TagSource.Manual, ManualValue = "1" }]
            };
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

    static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("断言失败");
        }
    }

    static void AssertFalse(bool condition) => AssertTrue(!condition);
}
