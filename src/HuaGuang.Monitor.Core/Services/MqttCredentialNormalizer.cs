namespace HuaGuang.Monitor.Services;

/// <summary>MQTT 账号规范化（Excel/界面粘贴常带尾随空白）。</summary>
public static class MqttCredentialNormalizer
{
    public static string NormalizeUsername(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public static string NormalizePassword(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.TrimEnd('\r', '\n', '\t', ' ', '\u00A0', '\u200B', '\uFEFF');
    }
}
