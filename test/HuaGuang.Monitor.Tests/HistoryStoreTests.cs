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
    public void Relocate_writes_to_new_directory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-old-{Guid.NewGuid():N}.db");
        var custom = Path.Combine(Path.GetTempPath(), $"huaguang-history-custom-{Guid.NewGuid():N}");
        try
        {
            var store = new HistoryStore(dbPath);
            store.Relocate(custom, Path.Combine(custom, "history.db"));
            store.InitializeAsync().GetAwaiter().GetResult();
            store.AppendAsync(Sample("自定义目录", DateTimeOffset.Now)).GetAwaiter().GetResult();
            Assert.True(Directory.GetFiles(custom, "*.db").Length >= 1);
            Assert.False(File.Exists(dbPath));
        }
        finally
        {
            Cleanup(dbPath);
            if (Directory.Exists(custom))
            {
                Directory.Delete(custom, true);
            }
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
            Assert.Equal(now, table.Rows[0].RecordedAt);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void One_day_retention_keeps_yesterday_and_drops_the_day_before()
    {
        var now = DateTimeOffset.Now;
        var cutoff = HistoryRecorder.RetentionCutoff(now, 1);
        Assert.Equal(now.ToLocalTime().Date.AddDays(-1), cutoff.DateTime);
        Assert.Equal(TimeSpan.Zero, cutoff.TimeOfDay);

        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-retain-{Guid.NewGuid():N}.db");
        try
        {
            var store = new HistoryStore(dbPath);
            store.InitializeAsync().GetAwaiter().GetResult();
            store.AppendAsync(Sample("今天", now)).GetAwaiter().GetResult();
            store.AppendAsync(Sample("昨天", now.AddDays(-1))).GetAwaiter().GetResult();
            store.AppendAsync(Sample("前天", now.AddDays(-2))).GetAwaiter().GetResult();

            var deleted = store.PruneOlderThanAsync(cutoff).GetAwaiter().GetResult();
            Assert.True(deleted >= 1);

            var rows = store.QueryAsync(new HistoryQuery
            {
                From = now.AddDays(-3),
                To = now.AddHours(1),
                Limit = 10
            }).GetAwaiter().GetResult();
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row.DeviceId == "今天");
            Assert.Contains(rows, row => row.DeviceId == "昨天");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public void Daily_prune_waits_until_after_local_midnight()
    {
        var now = DateTimeOffset.Now;
        var delay = HistoryRecorder.DelayUntilAfterLocalMidnight(now);
        var due = now + delay;
        var next = now.ToLocalTime().Date.AddDays(1).AddMinutes(1);
        Assert.Equal(next, due.ToLocalTime().DateTime);
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

public sealed class HistoryDirectoryPathTests
{
    [Fact]
    public void Rejects_android_document_tree_uri()
    {
        Assert.False(AppPaths.IsUsableHistoryDirectory(null));
        Assert.False(AppPaths.IsUsableHistoryDirectory(""));
        Assert.False(AppPaths.IsUsableHistoryDirectory(
            "content://com.android.externalstorage.documents/tree/primary%3AData"));
        Assert.True(AppPaths.IsUsableHistoryDirectory(@"F:\Data\history"));
    }
}
