using System.Collections.Concurrent;
using BrickBot.Modules.Core.Helpers;

namespace BrickBot.Modules.Database.Services;

public sealed class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly IMigrationRunner _runner;
    private readonly ILogHelper _logger;

    /// <summary>Per-profile gate so concurrent first-time accesses run migrations once.</summary>
    private readonly ConcurrentDictionary<string, Task> _gates = new(StringComparer.OrdinalIgnoreCase);

    public DatabaseMigrationService(IMigrationRunner runner, ILogHelper logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public Task EnsureMigratedAsync(string profileId)
    {
        return _gates.GetOrAdd(profileId, async pid =>
        {
            try
            {
                if (await _runner.IsDatabaseUpToDateAsync(pid).ConfigureAwait(false))
                {
                    return;
                }
                _logger.Info($"Profile {pid}: applying pending migrations", "DatabaseMigrationService");
                await _runner.MigrateToLatestAsync(pid).ConfigureAwait(false);
            }
            catch
            {
                // Drop the cached failed task so the next caller retries — otherwise a transient
                // failure permanently bricks DB access for the rest of the session.
                _gates.TryRemove(pid, out _);
                throw;
            }
        });
    }
}
