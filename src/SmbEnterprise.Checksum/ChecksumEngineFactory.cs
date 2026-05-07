using SmbEnterprise.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace SmbEnterprise.Checksum;

/// <summary>Factory that creates the appropriate IChecksumEngine for a given algorithm.</summary>
public static class ChecksumEngineFactory
{
    public static IChecksumEngine Create(ChecksumAlgorithm algorithm) => algorithm switch
    {
        ChecksumAlgorithm.XxHash64 => new XxHash64ChecksumEngine(),
        ChecksumAlgorithm.Crc32 => new Crc32ChecksumEngine(),
        ChecksumAlgorithm.Sha256 => new Sha256ChecksumEngine(),
        ChecksumAlgorithm.Md5 => new Md5ChecksumEngine(),
        _ => throw new NotSupportedException($"Algorithm not supported: {algorithm}")
    };
}

public static class ChecksumServiceCollectionExtensions
{
    public static IServiceCollection AddChecksumEngine(
        this IServiceCollection services,
        ChecksumAlgorithm algorithm = ChecksumAlgorithm.XxHash64)
    {
        services.AddSingleton<IChecksumEngine>(_ => ChecksumEngineFactory.Create(algorithm));
        services.AddSingleton<TransferVerifier>();
        return services;
    }
}
