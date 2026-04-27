using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Database.Services;
using BrickBot.Modules.Recording.Entities;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BrickBot.Modules.Recording.Services;

public sealed class RecordingRepository : IRecordingRepository
{
    private readonly IGlobalPathService _globalPaths;
    private readonly IDatabaseMigrationService _migrations;
    private readonly ILogHelper _logger;

    public RecordingRepository(IGlobalPathService globalPaths, IDatabaseMigrationService migrations, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _migrations = migrations;
        _logger = logger;
    }

    public async Task<List<RecordingEntity>> ListAsync(string profileId)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var rows = await conn.QueryAsync<RecordingEntity>(
            "SELECT * FROM Recordings ORDER BY CreatedAt DESC").ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<RecordingEntity?> GetByIdAsync(string profileId, string id)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        return await conn.QuerySingleOrDefaultAsync<RecordingEntity>(
            "SELECT * FROM Recordings WHERE Id = @id", new { id }).ConfigureAwait(false);
    }

    public async Task UpsertAsync(string profileId, RecordingEntity entity)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        if (entity.CreatedAt == default) entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await using var conn = OpenConnection(profileId);
        const string sql = @"
            INSERT INTO Recordings (Id, Name, Description, WindowTitle, Width, Height, FrameCount, IntervalMs, CreatedAt, UpdatedAt)
            VALUES (@Id, @Name, @Description, @WindowTitle, @Width, @Height, @FrameCount, @IntervalMs, @CreatedAt, @UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                WindowTitle = excluded.WindowTitle,
                Width = excluded.Width,
                Height = excluded.Height,
                FrameCount = excluded.FrameCount,
                IntervalMs = excluded.IntervalMs,
                UpdatedAt = excluded.UpdatedAt;";
        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string profileId, string id)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var n = await conn.ExecuteAsync("DELETE FROM Recordings WHERE Id = @id", new { id }).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<List<RecordingFrameEntity>> ListFramesAsync(string profileId, string recordingId)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var rows = await conn.QueryAsync<RecordingFrameEntity>(
            "SELECT * FROM RecordingFrames WHERE RecordingId = @recordingId ORDER BY FrameIndex",
            new { recordingId }).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task UpsertFrameAsync(string profileId, RecordingFrameEntity entity)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        if (entity.CapturedAt == default) entity.CapturedAt = DateTime.UtcNow;
        await using var conn = OpenConnection(profileId);
        const string sql = @"
            INSERT INTO RecordingFrames (Id, RecordingId, FrameIndex, Width, Height, CapturedAt)
            VALUES (@Id, @RecordingId, @FrameIndex, @Width, @Height, @CapturedAt)
            ON CONFLICT(Id) DO UPDATE SET
                FrameIndex = excluded.FrameIndex,
                Width = excluded.Width,
                Height = excluded.Height,
                CapturedAt = excluded.CapturedAt;";
        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);
    }

    public async Task DeleteFramesForRecordingAsync(string profileId, string recordingId)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        await conn.ExecuteAsync(
            "DELETE FROM RecordingFrames WHERE RecordingId = @recordingId",
            new { recordingId }).ConfigureAwait(false);
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
