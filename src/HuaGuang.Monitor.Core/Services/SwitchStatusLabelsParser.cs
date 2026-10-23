using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>解析点位「开关状态文案」列：<c>0=关;1=开;2=待机</c>。</summary>
public static class SwitchStatusLabelsParser
{
    public const string DefaultRunStatusLabels = "0=关;1=开;2=待机";
    public const string HandAutoCleanLabels = "0=手动模式;1=自动模式;2=自动中;3=清洗模式;4=清洗中";

    public static readonly IReadOnlyDictionary<int, string> DefaultRunStatusMap =
        new Dictionary<int, string>
        {
            [0] = "关",
            [1] = "开",
            [2] = "待机"
        };

    public static bool TryParse(string? text, out Dictionary<int, string> map, out string error)
    {
        map = new Dictionary<int, string>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split([';', '|', '\n', '\r', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            var sep = part.IndexOfAny(['=', '：', ':']);
            if (sep <= 0)
            {
                error = $"无效状态项「{part}」，请使用 0=文案 格式。";
                map.Clear();
                return false;
            }

            var codeText = part[..sep].Trim();
            var label = part[(sep + 1)..].Trim();
            if (!int.TryParse(codeText, out var code))
            {
                error = $"状态码「{codeText}」不是整数。";
                map.Clear();
                return false;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                error = $"状态码 {code} 缺少文案。";
                map.Clear();
                return false;
            }

            map[code] = label;
        }

        return true;
    }

    public static string Normalize(string? text)
    {
        if (!TryParse(text, out var map, out _) || map.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(';', map.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
    }

    public static string FormatLabel(IReadOnlyDictionary<int, string> map, int code) =>
        map.TryGetValue(code, out var label) ? label : $"未知({code})";
}
