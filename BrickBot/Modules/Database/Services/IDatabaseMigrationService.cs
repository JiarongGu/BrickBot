namespace BrickBot.Modules.Database.Services;

/// <summary>
/// Coordinates startup migrations for a profile's database. Idempotent — safe to call
/// every time the active profile changes; cheap when nothing's pending.
/// </summary>
public interface IDatabaseMigrationService
{
    Task EnsureMigratedAsync(string profileId);
}
