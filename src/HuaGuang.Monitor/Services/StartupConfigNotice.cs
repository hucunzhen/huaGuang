using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 首屏就绪后提示产线加载错误/告警（避免 WinUI 在 XamlRoot 未就绪时弹窗崩溃）。
/// </summary>
public static class StartupConfigNotice
{
    static int _shown;

    public static async Task TryShowAsync(Page host)
    {
        if (Interlocked.Exchange(ref _shown, 1) != 0)
        {
            return;
        }

        var store = MauiProgram.Services.GetRequiredService<SettingsStore>();
        var error = store.LastLoadError;
        var warnings = store.Current.ConfigLoadWarnings;
        var needLine = !LineConfigPaths.HasConfirmedActiveLine();
        if (!needLine && string.IsNullOrWhiteSpace(error) && warnings.Count == 0)
        {
            Interlocked.Exchange(ref _shown, 0);
            return;
        }

        if (needLine
            && string.IsNullOrWhiteSpace(error)
            && warnings.Count == 0
            && MonitorProcessInstance.IsIsolated)
        {
            return;
        }

        if (!await WaitForPageAlertHostAsync(host).ConfigureAwait(true))
        {
            CrashExitLogger.Record(
                "StartupConfigNotice",
                null,
                fatal: false,
                "无法显示配置提示（页面未就绪），详见日志");
            return;
        }

        try
        {
            if (needLine && string.IsNullOrWhiteSpace(error) && warnings.Count == 0)
            {
                var goSettings = await host.DisplayAlert(
                    "请选择产线",
                    "首次安装后请先到「设置」选择产线。选择之前不会自动采集或订阅。",
                    "去设置",
                    "稍后").ConfigureAwait(true);
                if (goSettings && Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync("//settings").ConfigureAwait(true);
                }

                return;
            }

            var body = BuildBody(error, warnings, needLine);
            await host.DisplayAlert("产线配置需要检查", body, "知道了").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            CrashExitLogger.Record("StartupConfigNotice.DisplayAlert", ex, fatal: false);
        }
    }

    static string BuildBody(string? error, IReadOnlyList<string> warnings, bool needLine)
    {
        var body = new System.Text.StringBuilder();
        if (needLine)
        {
            body.AppendLine("尚未选择产线，首次安装后不会自动采集。请到「设置」选择产线。");
            body.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            body.AppendLine(error);
            body.AppendLine();
            if (error.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || error.Contains("正由另一进程使用", StringComparison.OrdinalIgnoreCase))
            {
                body.AppendLine("产线 Excel 可能被 Excel 或其它程序占用，请关闭后重启监控。");
                body.AppendLine();
            }

            body.AppendLine("请到「设置」修正 PLC 协议与点表，或直接编辑产线 Excel。");
        }

        if (warnings.Count > 0)
        {
            body.AppendLine();
            body.Append("配置告警（部分点位已禁用）：");
            body.AppendLine();
            foreach (var warning in warnings.Take(5))
            {
                body.AppendLine("• " + warning);
            }

            if (warnings.Count > 5)
            {
                body.AppendLine($"… 另有 {warnings.Count - 5} 条，见运行日志。");
            }
        }

        body.AppendLine();
        body.Append("日志目录：");
        body.AppendLine(AppPaths.LogDirectory);
        return body.ToString();
    }

    static async Task<bool> WaitForPageAlertHostAsync(Page host)
    {
        for (var i = 0; i < 200; i++)
        {
            if (CanShowWinUiAlert(host))
            {
                await Task.Delay(100).ConfigureAwait(true);
                return CanShowWinUiAlert(host);
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        return CanShowWinUiAlert(host);
    }

    static bool CanShowWinUiAlert(Page host)
    {
        if (host.Handler is null || Shell.Current is null)
        {
            return false;
        }

#if WINDOWS
        var window = host.Window ?? Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            return native.Content?.XamlRoot is not null;
        }

        return false;
#else
        return true;
#endif
    }
}
