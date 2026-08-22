using System.Globalization;
using Microsoft.Data.Sqlite;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class HistoryStore
{
    readonly string _databasePath;
    readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS telemetry_samples (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    recorded_at TEXT NOT NULL,
                    source_timestamp TEXT,
                    device_id TEXT NOT NULL,
                    source_topic TEXT,
                    operation_mode TEXT NOT NULL,
                    quality TEXT,
                    plc_host TEXT,
                    simulator INTEGER,
                    payload_json TEXT
                );

                CREATE TABLE IF NOT EXISTS tag_values (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sample_id INTEGER NOT NULL,
                    tag_id TEXT,
                    tag_name TEXT NOT NULL,
                    unit TEXT,
                    value_real REAL,
                    value_text TEXT,
                    value_kind TEXT NOT NULL,
                    quality TEXT,
                    FOREIGN KEY(sample_id) REFERENCES telemetry_samples(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_samples_recorded_at ON telemetry_samples(recorded_at);
                CREATE INDEX IF NOT EXISTS idx_samples_device ON telemetry_samples(device_id, recorded_at);
                CREATE INDEX IF NOT EXISTS idx_tag_values_sample ON tag_values(sample_id);
                """;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> AppendAsync(HistorySampleWriteRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

            long sampleId;
            await using (var insertSample = connection.CreateCommand())
            {
                insertSample.Transaction = transaction;
                insertSample.CommandText =
                    """
                    INSERT INTO telemetry_samples
                    (recorded_at, source_timestamp, device_id, source_topic, operation_mode, quality, plc_host, simulator, payload_json)
                    VALUES ($recorded_at, $source_timestamp, $device_id, $source_topic, $operation_mode, $quality, $plc_host, $simulator, $payload_json);
                    SELECT last_insert_rowid();
                    """;
                insertSample.Parameters.AddWithValue("$recorded_at", request.RecordedAt.UtcDateTime.ToString("O"));
                insertSample.Parameters.AddWithValue("$source_timestamp", ToDbDateTime(request.SourceTimestamp));
                insertSample.Parameters.AddWithValue("$device_id", request.DeviceId);
                insertSample.Parameters.AddWithValue("$source_topic", (object?)request.SourceTopic ?? DBNull.Value);
                insertSample.Parameters.AddWithValue("$operation_mode", request.OperationMode.ToString());
                insertSample.Parameters.AddWithValue("$quality", (object?)request.Quality ?? DBNull.Value);
                insertSample.Parameters.AddWithValue("$plc_host", (object?)request.PlcHost ?? DBNull.Value);
                insertSample.Parameters.AddWithValue("$simulator", request.Simulator.HasValue ? request.Simulator.Value ? 1 : 0 : DBNull.Value);
                insertSample.Parameters.AddWithValue("$payload_json", (object?)request.PayloadJson ?? DBNull.Value);
                sampleId = (long)(await insertSample.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
            }

            await using (var insertTag = connection.CreateCommand())
            {
                insertTag.Transaction = transaction;
                insertTag.CommandText =
                    """
                    INSERT INTO tag_values
                    (sample_id, tag_id, tag_name, unit, value_real, value_text, value_kind, quality)
                    VALUES ($sample_id, $tag_id, $tag_name, $unit, $value_real, $value_text, $value_kind, $quality);
                    """;
                var sampleParam = insertTag.Parameters.Add("$sample_id", SqliteType.Integer);
                var tagIdParam = insertTag.Parameters.Add("$tag_id", SqliteType.Text);
                var tagNameParam = insertTag.Parameters.Add("$tag_name", SqliteType.Text);
                var unitParam = insertTag.Parameters.Add("$unit", SqliteType.Text);
                var valueRealParam = insertTag.Parameters.Add("$value_real", SqliteType.Real);
                var valueTextParam = insertTag.Parameters.Add("$value_text", SqliteType.Text);
                var valueKindParam = insertTag.Parameters.Add("$value_kind", SqliteType.Text);
                var qualityParam = insertTag.Parameters.Add("$quality", SqliteType.Text);

                foreach (var tag in request.Tags)
                {
                    var encoded = EncodeValue(tag.Value);
                    sampleParam.Value = sampleId;
                    tagIdParam.Value = (object?)tag.TagId ?? DBNull.Value;
                    tagNameParam.Value = tag.Name;
                    unitParam.Value = string.IsNullOrWhiteSpace(tag.Unit) ? DBNull.Value : tag.Unit;
                    valueRealParam.Value = encoded.Real.HasValue ? encoded.Real.Value : DBNull.Value;
                    valueTextParam.Value = encoded.Text is null ? DBNull.Value : encoded.Text;
                    valueKindParam.Value = encoded.Kind;
                    qualityParam.Value = tag.Quality;
                    await insertTag.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            return sampleId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistorySampleSummary>> QueryAsync(HistoryQuery query)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT s.id, s.recorded_at, s.device_id, s.operation_mode, s.quality, s.source_topic,
                       COUNT(t.id) AS tag_count
                FROM telemetry_samples s
                LEFT JOIN tag_values t ON t.sample_id = s.id
                WHERE s.recorded_at >= $from AND s.recorded_at <= $to
                """;
            command.Parameters.AddWithValue("$from", query.From.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$to", query.To.UtcDateTime.ToString("O"));

            if (!string.IsNullOrWhiteSpace(query.DeviceId))
            {
                command.CommandText += " AND s.device_id = $device_id";
                command.Parameters.AddWithValue("$device_id", query.DeviceId);
            }

            command.CommandText +=
                """
                 GROUP BY s.id
                 ORDER BY s.recorded_at DESC
                 LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", query.Limit);

            var results = new List<HistorySampleSummary>();
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var mode = Enum.TryParse<AppOperationMode>(reader.GetString(3), out var parsed)
                    ? parsed
                    : AppOperationMode.Acquisition;
                results.Add(new HistorySampleSummary
                {
                    Id = reader.GetInt64(0),
                    RecordedAt = ParseDbDateTime(reader.GetString(1)),
                    DeviceId = reader.GetString(2),
                    OperationModeLabel = ModeLabel(mode),
                    Quality = reader.IsDBNull(4) ? "—" : reader.GetString(4),
                    SourceTopic = reader.IsDBNull(5) ? null : reader.GetString(5),
                    TagCount = reader.GetInt32(6)
                });
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistorySampleDetail?> GetDetailAsync(long sampleId, int temperaturePrecision)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var sampleCommand = connection.CreateCommand();
            sampleCommand.CommandText =
                """
                SELECT id, recorded_at, source_timestamp, device_id, operation_mode, quality, source_topic, payload_json
                FROM telemetry_samples
                WHERE id = $id;
                """;
            sampleCommand.Parameters.AddWithValue("$id", sampleId);

            HistorySampleDetail? detail;
            await using (var reader = await sampleCommand.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (!await reader.ReadAsync().ConfigureAwait(false))
                {
                    return null;
                }

                var mode = Enum.TryParse<AppOperationMode>(reader.GetString(4), out var parsed)
                    ? parsed
                    : AppOperationMode.Acquisition;
                detail = new HistorySampleDetail
                {
                    Id = reader.GetInt64(0),
                    RecordedAt = ParseDbDateTime(reader.GetString(1)),
                    SourceTimestamp = reader.IsDBNull(2) ? null : ParseDbDateTime(reader.GetString(2)),
                    DeviceId = reader.GetString(3),
                    OperationModeLabel = ModeLabel(mode),
                    Quality = reader.IsDBNull(5) ? "—" : reader.GetString(5),
                    SourceTopic = reader.IsDBNull(6) ? null : reader.GetString(6),
                    PayloadJson = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
            }

            var tags = new List<HistoryTagValueRow>();
            await using (var tagCommand = connection.CreateCommand())
            {
                tagCommand.CommandText =
                    """
                    SELECT tag_name, unit, value_real, value_text, value_kind, quality
                    FROM tag_values
                    WHERE sample_id = $id
                    ORDER BY tag_name;
                    """;
                tagCommand.Parameters.AddWithValue("$id", sampleId);
                await using var reader = await tagCommand.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var tag = new PlcTag
                    {
                        Name = reader.GetString(0),
                        Unit = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        DataType = InferDataType(reader.GetString(4))
                    };
                    var value = DecodeValue(
                        reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetString(4));
                    tags.Add(new HistoryTagValueRow
                    {
                        TagName = tag.Name,
                        Unit = string.IsNullOrWhiteSpace(tag.Unit) ? null : tag.Unit,
                        DisplayValue = ValueFormatting.FormatDisplay(tag, value, temperaturePrecision),
                        Quality = reader.IsDBNull(5) ? "Good" : reader.GetString(5)
                    });
                }
            }

            return new HistorySampleDetail
            {
                Id = detail.Id,
                RecordedAt = detail.RecordedAt,
                SourceTimestamp = detail.SourceTimestamp,
                DeviceId = detail.DeviceId,
                OperationModeLabel = detail.OperationModeLabel,
                Quality = detail.Quality,
                SourceTopic = detail.SourceTopic,
                PayloadJson = detail.PayloadJson,
                Tags = tags
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetDeviceIdsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT device_id FROM telemetry_samples ORDER BY device_id;";
            var results = new List<string>();
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                results.Add(reader.GetString(0));
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoff)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM telemetry_samples WHERE recorded_at < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O"));
            return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(int SampleCount, DateTimeOffset? Oldest, DateTimeOffset? Newest)> GetStatsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*), MIN(recorded_at), MAX(recorded_at)
                FROM telemetry_samples;
                """;
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            if (!await reader.ReadAsync().ConfigureAwait(false))
            {
                return (0, null, null);
            }

            var count = reader.GetInt32(0);
            DateTimeOffset? oldest = reader.IsDBNull(1) ? null : ParseDbDateTime(reader.GetString(1));
            DateTimeOffset? newest = reader.IsDBNull(2) ? null : ParseDbDateTime(reader.GetString(2));
            return (count, oldest, newest);
        }
        finally
        {
            _gate.Release();
        }
    }

    SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    static string ModeLabel(AppOperationMode mode) =>
        mode == AppOperationMode.Subscribe ? "订阅" : "采集";

    static object? ToDbDateTime(DateTimeOffset? value) =>
        value.HasValue ? value.Value.UtcDateTime.ToString("O") : DBNull.Value;

    static DateTimeOffset ParseDbDateTime(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    static (double? Real, string? Text, string Kind) EncodeValue(object? value) =>
        value switch
        {
            null => (null, null, "null"),
            bool flag => (null, flag ? "true" : "false", "bool"),
            string text => (null, text, "string"),
            double number => (number, null, "number"),
            float number => (number, null, "number"),
            int number => (number, null, "number"),
            long number => (number, null, "number"),
            decimal number => ((double)number, null, "number"),
            _ => (null, Convert.ToString(value, CultureInfo.InvariantCulture), "string")
        };

    static object? DecodeValue(double? real, string? text, string kind) =>
        kind switch
        {
            "null" => null,
            "bool" => bool.TryParse(text, out var flag) && flag,
            "number" when real.HasValue => real.Value,
            _ => text
        };

    static TagDataType InferDataType(string kind) =>
        kind switch
        {
            "bool" => TagDataType.Bool,
            "string" => TagDataType.String,
            _ => TagDataType.Float32
        };
}
