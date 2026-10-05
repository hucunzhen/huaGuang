using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class HistoryStoreTests
{
    [Fact]
    public void Writes_query_and_deletes_across_daily_files()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-{Guid.NewGuid():N}.db");
        try
        {
            var store = new HistoryStore(dbPath);
            store.InitializeAsync().GetAwaiter().GetResult();

            var today = DateTimeOffset.Now;
            var yesterday = today.AddDays(-1);
            var todayId = store.AppendAsync(Sample("今日", today)).GetAwaiter().GetResult();
            var yesterdayId = store.AppendAsync(Sample("昨日", yesterday)).GetAwaiter().GetResult();
            Assert.True(todayId > 0);
            Assert.True(yesterdayId > 0);
            Assert.NotEqual(todayId, yesterdayId);

            var shardDir = Path.Combine(Path.GetDirectoryName(dbPath)!, Path.GetFileNameWithoutExtension(dbPath)!);
            Assert.True(Directory.GetFiles(shardDir, "*.db").Length >= 2);

            var query = new HistoryQuery
            {
                From = yesterday.AddHours(-1),
                To = today.AddHours(1),
                Limit = 10
            };
            Assert.Equal(2, store.CountMatchingAsync(query).GetAwaiter().GetResult());
            var rows = store.QueryAsync(query).GetAwaiter().GetResult();
            Assert.Equal(2, rows.Count);
            Assert.Equal("今日", rows[0].DeviceId);

            Assert.NotNull(store.GetDetailAsync(yesterdayId, 1).GetAwaiter().GetResult());
            Assert.True(store.DeleteSampleAsync(yesterdayId).GetAwaiter().GetResult());
            Assert.Null(store.GetDetailAsync(yesterdayId, 1).GetAwaiter().GetResult());
            Assert.Equal(1, store.CountMatchingAsync(query).GetAwaiter().GetResult());

            var cutoff = today.AddHours(-12);
            store.PruneOlderThanAsync(cutoff).GetAwaiter().GetResult();
            Assert.Equal(1, store.GetStatsAsync().GetAwaiter().GetResult().SampleCount);

            Assert.Equal(1, store.DeleteAllAsync().GetAwaiter().GetResult());
            Assert.Equal(0, store.GetStatsAsync().GetAwaiter().GetResult().SampleCount);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void QueryTable_for_one_device_omits_other_device_tags()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-device-{Guid.NewGuid():N}.db");
        try
        {
            var store = new HistoryStore(dbPath);
            store.InitializeAsync().GetAwaiter().GetResult();
            var now = DateTimeOffset.Now;
            store.AppendAsync(Sample("设备A", now, "车速", 1.5)).GetAwaiter().GetResult();
            store.AppendAsync(new HistorySampleWriteRequest
            {
                RecordedAt = now,
                DeviceId = "设备B",
                OperationMode = AppOperationMode.Acquisition,
                Quality = "Good",
                Tags =
                [
                    new TagSnapshot
                    {
                        TagId = "pressure",
                        Name = "压力",
                        Unit = "MPa",
                        Value = 1.2,
                        Quality = "Good",
                        Timestamp = now
                    }
                ]
            }).GetAwaiter().GetResult();

            var query = new HistoryQuery
            {
                From = now.AddHours(-1),
                To = now.AddHours(1),
                DeviceId = "设备B",
                Limit = 10
            };
            var table = store.QueryTableAsync(query, 1, ["车速", "压力", "没有的点"]).GetAwaiter().GetResult();
            Assert.Equal(["压力"], table.Columns.Select(column => column.TagName).ToArray());
            Assert.Single(table.Rows);
            Assert.Equal("1.2", table.Rows[0].TagCells[0].Text);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    static void Cleanup(string dbPath)
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

    static HistorySampleWriteRequest Sample(string deviceId, DateTimeOffset recordedAt, string tagName = "车速", double value = 1.5) => new()
    {
        RecordedAt = recordedAt,
        DeviceId = deviceId,
        OperationMode = AppOperationMode.Acquisition,
        Quality = "Good",
        Tags =
        [
            new TagSnapshot
            {
                TagId = "speed",
                Name = tagName,
                Value = value,
                Quality = "Good",
                Timestamp = recordedAt
            }
        ]
    };
}
