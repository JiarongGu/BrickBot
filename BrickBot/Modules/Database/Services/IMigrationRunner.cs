namespace BrickBot.Modules.Database.Services;

/// <summary>
/// Per-profile FluentMigrator runner. Each profile has its own SQLite file
/// (data/profiles/{id}/profile.db) so schema is upgraded independently. Mirrors the
/// D3dxSkinManager Fluent module pattern that this codebase locked in via
/// architecture-decisions.md.
/// </summary>
public interface IMigrationRunner
{
    Task MigrateToLatestAsync(string profileId);
    Task<bool> IsDatabaseUpToDateAsync(string profileId);
}
