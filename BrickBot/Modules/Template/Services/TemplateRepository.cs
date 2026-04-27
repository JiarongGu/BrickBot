using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Database.Services;
using BrickBot.Modules.Template.Entities;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BrickBot.Modules.Template.Services;

public sealed class TemplateRepository : ITemplateRepository
{
    private readonly IGlobalPathService _globalPaths;
    private readonly IDatabaseMigrationService _migrations;
    private readonly ILogHelper _logger;

    public TemplateRepository(
        IGlobalPathService globalPaths,
        IDatabaseMigrationService migrations,
        ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _migrations = migrations;
        _logger = logger;
    }

    public async Task<List<TemplateEntity>> ListAsync(string profileId)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var rows = await conn.QueryAsync<TemplateEntity>(
            "SELECT * FROM Templates ORDER BY Name COLLATE NOCASE").ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<TemplateEntity?> GetByIdAsync(string profileId, string id)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        return await conn.QuerySingleOrDefaultAsync<TemplateEntity>(
            "SELECT * FROM Templates WHERE Id = @id", new { id }).ConfigureAwait(false);
    }

    public async Task<TemplateEntity?> GetByNameAsync(string profileId, string name)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        return await conn.QueryFirstOrDefaultAsync<TemplateEntity>(
            "SELECT * FROM Templates WHERE Name = @name COLLATE NOCASE LIMIT 1",
            new { name }).ConfigureAwait(false);
    }

    public async Task UpsertAsync(string profileId, TemplateEntity entity)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);

        if (entity.CreatedAt == default) entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        const string sql = @"
            INSERT INTO Templates (Id, Name, Description, Width, Height, CreatedAt, UpdatedAt)
            VALUES (@Id, @Name, @Description, @Width, @Height, @CreatedAt, @UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                Width = excluded.Width,
                Height = excluded.Height,
                UpdatedAt = excluded.UpdatedAt;";
        await conn.ExecuteAsync(sql, entity).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string profileId, string id)
    {
        await _migrations.EnsureMigratedAsync(profileId).ConfigureAwait(false);
        await using var conn = OpenConnection(profileId);
        var n = await conn.ExecuteAsync("DELETE FROM Templates WHERE Id = @id", new { id }).ConfigureAwait(false);
        return n > 0;
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
