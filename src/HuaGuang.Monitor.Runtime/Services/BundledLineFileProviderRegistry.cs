namespace HuaGuang.Monitor.Services;

/// <summary>从应用安装包（如 Android assets）提供产线 Excel 模板。</summary>
public interface IBundledLineFileProvider
{
    bool TryCopyBundledLineFile(string lineName, string destinationPath);

    string? ResolveBundledTemplatePath(string lineName);
}

public static class BundledLineFileProviderRegistry
{
    static IBundledLineFileProvider? _provider;

    public static void Configure(IBundledLineFileProvider? provider) => _provider = provider;

    public static IBundledLineFileProvider? Current => _provider;
}
