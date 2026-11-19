using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HuaGuang.Monitor.Services;

/// <summary>订阅网页的登录会话。只活在订阅服务进程里，重启后需要重新登录。</summary>
static class SubscribeWebSessions
{
    public const string CookieName = "huaguang_web";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(6);

    static readonly ConcurrentDictionary<string, DateTimeOffset> Tokens = new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, DateTimeOffset> LastSeen = new(StringComparer.Ordinal);

    public static int ActiveCount
    {
        get
        {
            var cutoff = DateTimeOffset.UtcNow - ActiveWindow;
            var count = 0;
            foreach (var pair in LastSeen)
            {
                if (pair.Value < cutoff || !IsValid(pair.Key))
                {
                    continue;
                }

                count++;
            }

            return count;
        }
    }

    public static string Issue()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        Tokens[token] = now.Add(Lifetime);
        LastSeen[token] = now;
        return token;
    }

    public static void Touch(string? token) => Touch(token, DateTimeOffset.UtcNow);

    public static void Touch(string? token, DateTimeOffset seen)
    {
        if (!IsValid(token))
        {
            return;
        }

        LastSeen[token!] = seen;
    }

    public static bool IsValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !Tokens.TryGetValue(token, out var expires))
        {
            return false;
        }

        if (expires < DateTimeOffset.UtcNow)
        {
            Tokens.TryRemove(token, out _);
            return false;
        }

        return true;
    }

    public static void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            Tokens.TryRemove(token, out _);
            LastSeen.TryRemove(token, out _);
        }
    }

    public static string? ReadCookie(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = part[..eq].Trim();
            if (!string.Equals(name, CookieName, StringComparison.Ordinal))
            {
                continue;
            }

            return part[(eq + 1)..].Trim();
        }

        return null;
    }

    public static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]).Replace('+', ' ');
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }
}
