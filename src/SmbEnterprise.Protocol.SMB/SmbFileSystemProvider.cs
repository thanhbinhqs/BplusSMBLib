using SmbEnterprise.Core.Abstractions;
using SmbEnterprise.Protocol.SMB.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmbEnterprise.Protocol.SMB;

/// <summary>
/// Provider that creates SMB-backed IRemoteFileSystem instances.
/// Register via DI using AddSmbProvider().
/// </summary>
public sealed class SmbFileSystemProvider : IFileSystemProvider
{
    private readonly IServiceProvider _services;

    public string ProviderName => "SMB";

    public SmbFileSystemProvider(IServiceProvider services)
    {
        _services = services;
    }

    public IRemoteFileSystem CreateFileSystem()
    {
        var pool = _services.GetRequiredService<SmbSessionPool>();
        var logger = _services.GetRequiredService<ILogger<SmbFileSystem>>();
        return new SmbFileSystem(pool, logger);
    }
}

public static class SmbServiceCollectionExtensions
{
    public static IServiceCollection AddSmbProvider(
        this IServiceCollection services,
        Action<SmbSessionPoolOptions>? configurePool = null)
    {
        var poolOptions = new SmbSessionPoolOptions();
        configurePool?.Invoke(poolOptions);

        services.AddSingleton(poolOptions);
        services.AddSingleton<SmbSessionPool>();
        services.AddTransient<SmbFileSystem>();
        services.AddTransient<IFileSystemProvider, SmbFileSystemProvider>();

        return services;
    }
}
