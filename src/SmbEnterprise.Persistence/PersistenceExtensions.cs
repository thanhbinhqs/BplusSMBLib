using SmbEnterprise.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SmbEnterprise.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddSqlitePersistence(
        this IServiceCollection services,
        string databasePath = "smbjobs.db")
    {
        services.AddDbContextFactory<SmbJobsDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton<SqliteJobRepository>();
        return services;
    }
}
