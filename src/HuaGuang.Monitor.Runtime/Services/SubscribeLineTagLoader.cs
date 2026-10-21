using System.Collections.Concurrent;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>订阅跨产线点表：读各产线 Excel，按文件时间缓存。</summary>
public static class SubscribeLineTagLoader
{
    static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    public static void Register() =>
        SubscribeLineCatalog.LoadTags = Load;

    static IReadOnlyList<PlcTag>? Load(string lineName)
    {
        try
        {
            var path = LineConfigPaths.GetLineExcelPath(lineName);
            var ticks = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L;
            if (Cache.TryGetValue(lineName, out var cached) && cached.Ticks == ticks)
            {
                return cached.Tags;
            }

            IReadOnlyList<PlcTag> tags = File.Exists(path)
                ? LineExcelConfigService.LoadLineExcel(lineName, path).Tags
                : LineExcelConfigService.CreateSeedSettings(lineName).Tags;

            Cache[lineName] = new CacheEntry(ticks, tags);
            return tags;
        }
        catch
        {
            return null;
        }
    }

    readonly record struct CacheEntry(long Ticks, IReadOnlyList<PlcTag> Tags);
}
