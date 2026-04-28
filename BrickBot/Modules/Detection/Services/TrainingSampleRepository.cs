using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Database.Services;
using BrickBot.Modules.Detection.Entities;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BrickBot.Modules.Detection.Services;

public sealed class TrainingSampleRepository : ITrainingSampleRepository
{
    private readonly IGlobalPathService _globalPaths;
    private readonly IDatabaseMigrationService _migrations;
    private readonly ILogHelper _logger;

    public TrainingSampleRepository(IGlobalPathService globalPaths, IDatabaseMigrationService migrations, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _migrations = migrations;
        _logger = logger;
    }

    public async Task<List<TrainingSampleEntity>> ListByDetectionAsync(string profileId, string detectionId)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var rows = await conn.QueryAsync<TrainingSampleEntity>(
            "SELECT * FROM TrainingSamples WHERE DetectionId = @detectionId ORDER BY CapturedAt",
            new { detectionId }).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task UpsertAsync(string profileId, TrainingSampleEntity entity)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        if (entity.CapturedAt == default) entity.CapturedAt = DateTime.UtcNow;
        const string sql = @"
            INSERT INTO TrainingSamples (Id, DetectionId, Label, Note, Width, Height, CapturedAt, ObjectBoxJson, IsInit)
            VALUES (@Id, @DetectionId, @Label, @Note, @Width, @Height, @CapturedAt, @ObjectBoxJson, @IsInit)
            ON CONFLICT(Id) DO UPDATE SET
                DetectionId = excluded.DetectionId,
                Label = excluded.Label,
                Note = excluded.Note,
                Width = excluded.Width,
                Height = excluded.Height,
                CapturedAt = excluded.CapturedAt,
                ObjectBoxJson = excluded.ObjectBoxJson,
                IsInit = excluded.IsInit;";
        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string profileId, string id)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var n = await conn.ExecuteAsync("DELETE FROM TrainingSamples WHERE Id = @id", new { id }).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<int> DeleteByDetectionAsync(string profileId, string detectionId)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        return await conn.ExecuteAsync(
            "DELETE FROM TrainingSamples WHERE DetectionId = @detectionId",
            new { detectionId }).ConfigureAwait(false);
    }

    private SqliteConnection OpenConnection(string profileId)
    {
        var path = _globalPaths.GetProfileDatabasePath(profileId);
        var cs = path.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ? path : $"Data Source={path}";
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }
}
