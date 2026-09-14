using System.Globalization;
using System.Text.RegularExpressions;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Protocols;

public readonly record struct S7ResolvedAddress(
    S7MemoryArea Area,
    int DbNumber,
    int ByteOffset,
    int BitOffset,
    bool IsBit,
    string Normalized);

/// <summary>西门子 S7 地址解析（DB/M/I/Q 等）。</summary>
public static partial class SiemensS7AddressMapper
{
    public static bool TryResolve(string? input, TagDataType dataType, out S7ResolvedAddress resolved, out string error)
    {
        resolved = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "请填写 S7 地址，例如 DB1.DBD0、M0.0、IW0。";
            return false;
        }

        var text = NormalizeAddressText(input);
        if (TryResolveDataBlock(text, dataType, out resolved, out error))
        {
            return true;
        }

        if (TryResolveArea(text, "M", S7MemoryArea.Memory, dataType, out resolved, out error))
        {
            return true;
        }

        if (TryResolveArea(text, "I", S7MemoryArea.Input, dataType, out resolved, out error))
        {
            return true;
        }

        if (TryResolveArea(text, "Q", S7MemoryArea.Output, dataType, out resolved, out error))
        {
            return true;
        }

        error = "S7 地址格式无效。示例：IW64、ID64、QW4、DB1.DBD0、M0.0、I0.0、%IW64。";
        return false;
    }

    /// <summary>去掉 %、空格，并将德文 E/A 区映射为 I/Q（EW64 → IW64）。</summary>
    internal static string NormalizeAddressText(string? input)
    {
        var text = input?.Trim().ToUpperInvariant() ?? string.Empty;
        while (text.StartsWith('%'))
        {
            text = text[1..];
        }

        text = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        text = MapGermanAreaPrefix(text);
        return text;
    }

    static string MapGermanAreaPrefix(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (text[0] == 'E')
        {
            return "I" + text[1..];
        }

        if (text[0] == 'A' && text.Length > 1 && IsAreaTail(text[1]))
        {
            return "Q" + text[1..];
        }

        return text;
    }

    static bool IsAreaTail(char c) =>
        c is 'B' or 'W' or 'D' or '.' || char.IsDigit(c);

    public static void ApplyTo(PlcTag tag)
    {
        if (!TryResolve(tag.XinjeAddress, tag.DataType, out var resolved, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (resolved.IsBit)
        {
            tag.DataType = TagDataType.Bool;
        }
    }

    public static string Describe(in S7ResolvedAddress resolved) =>
        resolved.IsBit
            ? $"S7 {resolved.Area}  {resolved.Normalized} → 字节 {resolved.ByteOffset} 位 {resolved.BitOffset}"
            : resolved.Area == S7MemoryArea.DataBlock
                ? $"S7 DB{resolved.DbNumber}  {resolved.Normalized} → 字节 {resolved.ByteOffset}"
                : $"S7 {resolved.Area}  {resolved.Normalized} → 字节 {resolved.ByteOffset}";

    static bool TryResolveDataBlock(string text, TagDataType dataType, out S7ResolvedAddress resolved, out string error)
    {
        resolved = default;
        error = string.Empty;
        var bitMatch = DbBitRegex().Match(text);
        if (bitMatch.Success)
        {
            var db = ParseInt(bitMatch.Groups[1].Value);
            var byteOffset = ParseInt(bitMatch.Groups[2].Value);
            var bit = ParseInt(bitMatch.Groups[3].Value);
            if (bit is < 0 or > 7)
            {
                error = "位地址应为 0–7。";
                return false;
            }

            resolved = new S7ResolvedAddress(
                S7MemoryArea.DataBlock,
                db,
                byteOffset,
                bit,
                true,
                $"DB{db}.DBX{byteOffset}.{bit}");
            return true;
        }

        var typedMatch = DbTypedRegex().Match(text);
        if (typedMatch.Success)
        {
            var db = ParseInt(typedMatch.Groups[1].Value);
            var suffix = typedMatch.Groups[2].Value;
            var offset = ParseInt(typedMatch.Groups[3].Value);
            var (byteOffset, isBit, bit) = MapSuffixOffset(suffix, offset, dataType, out error);
            if (error.Length > 0)
            {
                return false;
            }

            resolved = new S7ResolvedAddress(
                S7MemoryArea.DataBlock,
                db,
                byteOffset,
                bit,
                isBit,
                $"DB{db}.DB{suffix}{offset}");
            return true;
        }

        return false;
    }

    static bool TryResolveArea(
        string text,
        string prefix,
        S7MemoryArea area,
        TagDataType dataType,
        out S7ResolvedAddress resolved,
        out string error)
    {
        resolved = default;
        error = string.Empty;

        var bitMatch = AreaBitRegex(prefix).Match(text);
        if (bitMatch.Success)
        {
            var bitByteOffset = ParseInt(bitMatch.Groups[1].Value);
            var bitIndex = ParseInt(bitMatch.Groups[2].Value);
            if (bitIndex is < 0 or > 7)
            {
                error = "位地址应为 0–7。";
                return false;
            }

            resolved = new S7ResolvedAddress(area, 0, bitByteOffset, bitIndex, true, $"{prefix}{bitByteOffset}.{bitIndex}");
            return true;
        }

        var typedMatch = AreaTypedRegex(prefix).Match(text);
        if (!typedMatch.Success)
        {
            return false;
        }

        var suffix = typedMatch.Groups[1].Value;
        var offset = ParseInt(typedMatch.Groups[2].Value);
        var (byteOffset, isBit, bit) = MapSuffixOffset(suffix, offset, dataType, out error);
        if (error.Length > 0)
        {
            return false;
        }

        resolved = new S7ResolvedAddress(area, 0, byteOffset, bit, isBit, $"{prefix}{suffix}{offset}");
        return true;
    }

    static (int ByteOffset, bool IsBit, int BitOffset) MapSuffixOffset(
        string suffix,
        int offset,
        TagDataType dataType,
        out string error)
    {
        error = string.Empty;
        return suffix switch
        {
            "X" => (offset, true, 0),
            "B" => (offset, false, 0),
            "W" => (offset, false, 0),
            "D" => (offset, false, 0),
            "" when dataType == TagDataType.Bool => (offset, true, 0),
            "" => (offset, false, 0),
            _ => (0, false, 0)
        };
    }

    static int ParseInt(string text) =>
        int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^DB(\d+)\.DBX(\d+)\.(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DbBitRegex();

    [GeneratedRegex(@"^DB(\d+)\.DB([BWD]?)(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DbTypedRegex();

    static Regex AreaBitRegex(string prefix) =>
        new($"^{prefix}(\\d+)\\.(\\d+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static Regex AreaTypedRegex(string prefix) =>
        new($"^{prefix}([BWD]?)(\\d+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
