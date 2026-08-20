namespace HuaGuang.Monitor.Services;

public static class StartupRegistration
{
    public const string RegistryValueName = "IndustrialMonitor";
}

public sealed class NoOpStartupRegistration : IStartupRegistration
{
    public bool IsSupported => false;

    public bool IsRegistered => false;

    public void Apply(bool enabled)
    {
    }
}
