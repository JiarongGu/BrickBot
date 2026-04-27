using BrickBot.Modules.Database.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Database;

public static class DatabaseServiceExtensions
{
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IMigrationRunner, MigrationRunner>();
        services.TryAddSingleton<IDatabaseMigrationService, DatabaseMigrationService>();
        return services;
    }
}
