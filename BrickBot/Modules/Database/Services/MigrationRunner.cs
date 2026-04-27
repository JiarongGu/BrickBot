using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace BrickBot.Modules.Database.Services;

public sealed class MigrationRunner : IMigrationRunner
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;

    public MigrationRunner(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public Task MigrateToLatestAsync(string profileId)
    {
        var conn = ConnectionString(profileId);
        EnsureDatabaseDirectory(profileId);

        try
        {
            using var sp = BuildServices(conn);
            using var scope = sp.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<FluentMigrator.Runner.IMigrationRunner>();
            runner.MigrateUp();
            _logger.Info($"Profile {profileId}: migrations applied", "MigrationRunner");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.Error($"Migration failed for profile {profileId}: {ex.Message}", "MigrationRunner", ex);
            throw;
        }
    }

    public Task<bool> IsDatabaseUpToDateAsync(string profileId)
    {
        var conn = ConnectionString(profileId);
        EnsureDatabaseDirectory(profileId);

        try
        {
            using var sp = BuildServices(conn);
            using var scope = sp.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<FluentMigrator.Runner.IMigrationRunner>();
            var loader = scope.ServiceProvider.GetRequiredService<FluentMigrator.Runner.IVersionLoader>();
            var applied = loader.VersionInfo.AppliedMigrations();
            var pending = runner.MigrationLoader.LoadMigrations()
                .Where(m => !applied.Contains(m.Key));
            return Task.FromResult(!pending.Any());
        }
        catch (Exception ex)
        {
            // If we can't tell, treat as needs-migration so the next call repairs.
            _logger.Warn($"Unable to check pending migrations for profile {profileId}: {ex.Message}", "MigrationRunner");
            return Task.FromResult(false);
        }
    }

    private void EnsureDatabaseDirectory(string profileId)
    {
        var dir = _globalPaths.GetProfileDirectoryPath(profileId);
        Directory.CreateDirectory(dir);
    }

    private string ConnectionString(string profileId)
    {
        var path = _globalPaths.GetProfileDatabasePath(profileId);
        return path.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"Data Source={path}";
    }

    /// <summary>Standalone DI scope for FluentMigrator — kept self-contained per-call so we
    /// don't pollute the main app DI with the migration plumbing services.</summary>
    private static ServiceProvider BuildServices(string connectionString)
    {
        var asm = typeof(MigrationRunner).Assembly;
        return new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(asm).For.Migrations())
            .AddLogging(lb => { /* no console */ })
            .BuildServiceProvider(false);
    }
}
