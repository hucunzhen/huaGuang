namespace HuaGuang.Monitor.Services;

public static class DesktopShortcutCatalog
{
    public const string StartMenuGroupName = "工业监控";

    public static IReadOnlyList<DesktopShortcutDefinition> Definitions { get; } =
    [
        new("sub", "订阅大屏", "--instance sub --mode subscribe"),
        new("acq-xianhe", "先河采集", "--instance acq-xianhe --line xianhe --mode acquisition"),
        new("acq-huadi", "华迪采集", "--instance acq-huadi --line huadi --mode acquisition"),
        new("acq-safen", "撒粉采集", "--instance acq-safen --line safen --mode acquisition"),
        new("acq-pingban", "平板采集", "--instance acq-pingban --line pingban --mode acquisition"),
        new("acq-cyhy", "C型火焰采集", "--instance acq-cyhy --line cyhy --mode acquisition"),
        new("acq-s7", "S7采集", "--instance acq-s7 --line s7 --mode acquisition")
    ];
}

public sealed class NoOpDesktopShortcutService : IDesktopShortcutService
{
    public bool IsSupported => false;

    public IReadOnlyList<DesktopShortcutDefinition> Definitions => [];

    public DesktopShortcutPresence GetPresence(string id) => new(false, false);

    public void Add(string id)
    {
    }

    public void Remove(string id)
    {
    }
}
