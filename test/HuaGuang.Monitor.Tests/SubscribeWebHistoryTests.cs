using ClosedXML.Excel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class SubscribeWebHistoryTests
{
    [Fact]
    public void Missing_range_defaults_to_last_24_hours()
    {
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.FromHours(8));
        var ok = SubscribeWebHistory.TryParse(new Uri("http://localhost/api/history"), now, out var filter, out var error);
        Assert.True(ok, error);
        Assert.Equal(now.AddHours(-24), filter.From);
        Assert.Equal(now, filter.To);
        Assert.Null(filter.DeviceId);
        Assert.Equal(1, filter.Page);
    }

    [Fact]
    public void Rejects_range_longer_than_31_days()
    {
        var url = new Uri("http://localhost/api/history?from=2026-10-01T00:00&to=2026-11-02T00:01");
        var ok = SubscribeWebHistory.TryParse(url, DateTimeOffset.Now, out _, out var error);
        Assert.False(ok);
        Assert.Contains("31", error);
    }

    [Fact]
    public void All_devices_token_is_not_a_filter()
    {
        var url = new Uri("http://localhost/api/history?device=%E5%85%A8%E9%83%A8%E8%AE%BE%E5%A4%87&from=2026-11-16T08:00&to=2026-11-16T09:00");
        var ok = SubscribeWebHistory.TryParse(url, DateTimeOffset.Now, out var filter, out var error);
        Assert.True(ok, error);
        Assert.Null(filter.DeviceId);
    }

    [Fact]
    public void Export_refusal_covers_empty_too_many_and_missing_tag()
    {
        var filter = new SubscribeWebHistoryFilter
        {
            From = DateTimeOffset.Now.AddHours(-1),
            To = DateTimeOffset.Now,
            Tag = "不存在"
        };
        Assert.Equal("当前筛选条件下没有可导出的记录。", SubscribeWebHistory.ExportRefusal(filter, 0, 0));
        Assert.Contains("20000", SubscribeWebHistory.ExportRefusal(filter, 20_001, 1));
        Assert.Contains("不存在", SubscribeWebHistory.ExportRefusal(filter, 3, 0));
    }

    [Fact]
    public async Task Query_and_export_keep_selected_device_and_tag()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-web-history-{Guid.NewGuid():N}.db");
        try
        {
            var store = new HistoryStore(dbPath);
            await store.InitializeAsync();
            var now = DateTimeOffset.Now;
            await store.AppendAsync(Sample("起毛机", now, ("车速", 12.5), ("压力", 3)));
            await store.AppendAsync(Sample("烘焙机", now, ("车速", 1), ("压力", 8)));
            var settings = new AppSettings
            {
                TemperaturePrecision = 1,
                EnableHistoryRecording = true,
                HistoryRetentionDays = 7,
                Tags =
                [
                    new PlcTag { Name = "车速", Unit = "m/min", Enabled = true, DataType = TagDataType.Float32, DisplayPrecision = 1 },
                    new PlcTag { Name = "压力", Unit = "kPa", Enabled = true, DataType = TagDataType.Float32, DisplayPrecision = 1 }
                ]
            };
            var filter = new SubscribeWebHistoryFilter
            {
                From = now.AddHours(-1),
                To = now.AddHours(1),
                DeviceId = "起毛机",
                Tag = "车速",
                Page = 1
            };

            var page = await SubscribeWebHistory.QueryAsync(store, settings, filter);
            Assert.Equal(1, page.Total);
            Assert.Equal("起毛机", Assert.Single(page.Rows).Device);
            Assert.Contains(page.Columns, column => column.Contains("车速"));
            Assert.DoesNotContain(page.Columns, column => column.Contains("压力"));

            using var exported = await SubscribeWebHistory.ExportAsync(store, settings, filter, CancellationToken.None);
            using var workbook = new XLWorkbook(exported.Path);
            var sheet = workbook.Worksheet("历史数据");
            Assert.Contains("车速", sheet.Cell(1, 3).GetString());
            Assert.Equal("起毛机", sheet.Cell(2, 2).GetString());
            Assert.True(sheet.Cell(1, 4).IsEmpty());
            Assert.True(sheet.Cell(3, 1).IsEmpty());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var shardDir = Path.Combine(Path.GetDirectoryName(dbPath)!, Path.GetFileNameWithoutExtension(dbPath)!);
            if (Directory.Exists(shardDir))
            {
                Directory.Delete(shardDir, true);
            }

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    static HistorySampleWriteRequest Sample(string deviceId, DateTimeOffset recordedAt, params (string Name, double Value)[] tags) =>
        new()
        {
            RecordedAt = recordedAt,
            DeviceId = deviceId,
            OperationMode = AppOperationMode.Subscribe,
            Quality = "Good",
            Tags = tags.Select(tag => new TagSnapshot
            {
                TagId = tag.Name,
                Name = tag.Name,
                Value = tag.Value,
                Quality = "Good",
                Timestamp = recordedAt
            }).ToList()
        };
}
