using ClosedXML.Excel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class HistoryExcelWriterTests
{
    [Fact]
    public void Writes_time_numbers_text_and_blanks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"huaguang-history-xlsx-{Guid.NewGuid():N}.xlsx");
        var recorded = new DateTimeOffset(2026, 11, 9, 5, 5, 0, TimeSpan.Zero);
        var columns = new List<HistoryTableColumn>
        {
            new() { TagName = "车速", HeaderText = "车速(m/min)" },
            new() { TagName = "状态", HeaderText = "状态" },
            new() { TagName = "压力", HeaderText = "压力" }
        };

        try
        {
            using (var writer = new HistoryExcelWriter(columns))
            {
                writer.AddRows(
                [
                    new HistoryTableRow
                    {
                        RecordedAt = recorded,
                        DeviceId = "定型机10号",
                        TagCells =
                        [
                            new HistoryTableCell { Text = "12.50" },
                            new HistoryTableCell { Text = "自动中" },
                            new HistoryTableCell { Text = "—" }
                        ]
                    }
                ]);
                writer.Save(path);
            }

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("历史数据");
            Assert.Equal("时间", sheet.Cell(1, 1).GetString());
            Assert.Equal("设备", sheet.Cell(1, 2).GetString());
            Assert.Equal("车速(m/min)", sheet.Cell(1, 3).GetString());
            Assert.Equal(recorded.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), sheet.Cell(2, 1).GetString());
            Assert.Equal("定型机10号", sheet.Cell(2, 2).GetString());
            Assert.Equal(12.5, sheet.Cell(2, 3).GetDouble());
            Assert.Equal("自动中", sheet.Cell(2, 4).GetString());
            Assert.True(sheet.Cell(2, 5).IsEmpty());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Export_pages_cover_the_whole_device_filter()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-export-{Guid.NewGuid():N}.db");
        var xlsxPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            var store = new HistoryStore(dbPath);
            store.InitializeAsync().GetAwaiter().GetResult();
            var start = new DateTimeOffset(2026, 11, 9, 1, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 3; i++)
            {
                store.AppendAsync(Sample("线1", start.AddMinutes(i), i + 0.5)).GetAwaiter().GetResult();
            }

            store.AppendAsync(Sample("线2", start.AddMinutes(5), 9)).GetAwaiter().GetResult();

            var query = new HistoryQuery
            {
                From = start.AddHours(-1),
                To = start.AddHours(1),
                DeviceId = "线1"
            };
            const int pageSize = 2;
            HistoryExcelWriter? writer = null;
            IReadOnlyList<HistoryTableColumn>? columns = null;
            var offset = 0;
            var written = 0;
            try
            {
                while (true)
                {
                    var table = store.QueryTableAsync(
                        new HistoryQuery
                        {
                            From = query.From,
                            To = query.To,
                            DeviceId = query.DeviceId,
                            Limit = pageSize,
                            Offset = offset
                        },
                        temperaturePrecision: 1,
                        preferredTagOrder: ["车速"],
                        fixedColumns: columns).GetAwaiter().GetResult();
                    if (table.Rows.Count == 0)
                    {
                        break;
                    }

                    columns ??= table.Columns;
                    writer ??= new HistoryExcelWriter(columns);
                    writer.AddRows(table.Rows);
                    written += table.Rows.Count;
                    offset += table.Rows.Count;
                    if (table.Rows.Count < pageSize)
                    {
                        break;
                    }
                }

                Assert.NotNull(writer);
                Assert.Equal(3, written);
                writer.Save(xlsxPath);
            }
            finally
            {
                writer?.Dispose();
            }

            using var workbook = new XLWorkbook(xlsxPath);
            var sheet = workbook.Worksheet("历史数据");
            Assert.Equal(3, sheet.LastRowUsed()!.RowNumber() - 1);
            Assert.Equal("线1", sheet.Cell(2, 2).GetString());
            Assert.Equal("线1", sheet.Cell(3, 2).GetString());
            Assert.Equal("线1", sheet.Cell(4, 2).GetString());
            Assert.Equal(2.5, sheet.Cell(2, 3).GetDouble());
            Assert.Equal(1.5, sheet.Cell(3, 3).GetDouble());
            Assert.Equal(0.5, sheet.Cell(4, 3).GetDouble());
            Assert.DoesNotContain(
                sheet.Column(2).CellsUsed().Select(cell => cell.GetString()),
                text => text == "线2");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(xlsxPath))
            {
                File.Delete(xlsxPath);
            }

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

    static HistorySampleWriteRequest Sample(string deviceId, DateTimeOffset recordedAt, double value) => new()
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
                Name = "车速",
                Value = value,
                Quality = "Good",
                Timestamp = recordedAt
            }
        ]
    };
}
