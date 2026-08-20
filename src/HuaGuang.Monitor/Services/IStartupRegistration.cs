namespace HuaGuang.Monitor.Services;

public interface IStartupRegistration
{
    bool IsSupported { get; }

    bool IsRegistered { get; }

    void Apply(bool enabled);
}
