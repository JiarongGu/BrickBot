using System.Text.Json;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Database.Services;
using BrickBot.Modules.Detection.Entities;
using BrickBot.Modules.Detection.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BrickBot.Modules.Detection.Services;

/// <summary>
/// SQLite-backed detection store. Replaces the previous JSON-files-on-disk implementation.
/// The full definition is stored as a JSON blob in <c>DefinitionJson</c>; indexed columns
/// (Name/Kind/Group/Enabled) drive listing without parsing every row.
///
/// The interface name is preserved (<see cref="IDetectionFileService"/>) so the rest of the
/// codebase doesn't churn — it's still the "detection store" service even though the backing
/// medium is now a database.
/// </summary>
public sealed class DetectionFileService : IDetectionFileService
{
    private readonly IGlobalPathService _globalPaths;
    private readonly IDatabaseMigrationService _migrations;
    private readonly ILogHelper _logger;
    private readonly JsonSerializerOptions _json;

    public DetectionFileService(
        IGlobalPathService globalPaths,
        IDatabaseMigrationService migrations,
        ILogHelper logger,
        JsonSerializerOptions json)
    {
        _globalPaths = globalPaths;
        _migrations = migrations;
        _logger = logger;
        _json = new JsonSerializerOptions(json)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public IReadOnlyList<DetectionDefinition> List(string profileId)
    {
        _migrations.EnsureMigratedAsync(profileId).GetAwaiter().GetResult();
        using var conn = OpenConnection(profileId);
        var rows = conn.Query<DetectionEntity>("SELECT * FROM Detections ORDER BY Name COLLATE NOCASE").ToList();
        return rows.Select(Deserialize).Where(d => d is not null).Cast<DetectionDefinition>().ToList();
    }

    public DetectionDefinition? Get(string profileId, string id)
    {
        _migrations.EnsureMigratedAsync(profileId).GetAwaiter().GetResult();
        using var conn = OpenConnection(profileId);
        var row = conn.QuerySingleOrDefault<DetectionEntity>(
            "SELECT * FROM Detections WHERE Id = @id", new { id });
        return row is null ? null : Deserialize(row);
    }

    public DetectionDefinition Save(string profileId, DetectionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new OperationException("DETECTION_NAME_REQUIRED");

        if (string.IsNullOrWhiteSpace(definition.Id))
            definition.Id = SlugifyName(definition.Name);
        ValidateId(definition.Id);

        _migrations.EnsureMigratedAsync(profileId).GetAwaiter().GetResult();

        var entity = new DetectionEntity
        {
            Id = definition.Id,
            Name = definition.Name,
            Kind = ToCamelCase(definition.Kind.ToString()),
            Group = definition.Group,
            Enabled = definition.Enabled ? 1 : 0,
            DefinitionJson = JsonSerializer.Serialize(definition, _json),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        using var conn = OpenConnection(profileId);
        const string sql = @"
            INSERT INTO Detections (Id, Name, Kind, [Group], Enabled, DefinitionJson, CreatedAt, UpdatedAt)
            VALUES (@Id, @Name, @Kind, @Group, @Enabled, @DefinitionJson, @CreatedAt, @UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Kind = excluded.Kind,
                [Group] = excluded.[Group],
                Enabled = excluded.Enabled,
                DefinitionJson = excluded.DefinitionJson,
                UpdatedAt = excluded.UpdatedAt;";
        conn.Execute(sql, entity);

        _logger.Info($"Saved detection {definition.Id} ({definition.Kind})", "Detection");
        return definition;
    }

    public void Delete(string profileId, string id)
    {
        _migrations.EnsureMigratedAsync(profileId).GetAwaiter().GetResult();
        using var conn = OpenConnection(profileId);
        var n = conn.Execute("DELETE FROM Detections WHERE Id = @id", new { id });
        if (n > 0) _logger.Info($"Deleted detection {id}", "Detection");
    }

    private DetectionDefinition? Deserialize(DetectionEntity row)
    {
        try
        {
            var def = JsonSerializer.Deserialize<DetectionDefinition>(row.DefinitionJson, _json);
            // Indexed columns are authoritative — back-fill them onto the deserialized object
            // so a row that was edited via SQL still reports the right id/name/kind/enabled.
            if (def is not null)
            {
                def.Id = row.Id;
                def.Name = row.Name;
                def.Group = row.Group;
                def.Enabled = row.Enabled != 0;
            }
            return def;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Skipping unreadable detection row {row.Id}: {ex.Message}", "Detection");
            return null;
        }
    }

    private SqliteConnection OpenConnection(string profileId)
    {
        var path = _globalPaths.GetProfileDatabasePath(profileId);
        var cs = path.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ? path : $"Data Source={path}";
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static string ToCamelCase(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new OperationException("DETECTION_ID_REQUIRED");
        foreach (var ch in id)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                throw new OperationException("DETECTION_INVALID_ID", new() { ["id"] = id });
        }
    }

    private static string SlugifyName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        var prevDash = false;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); prevDash = false; }
            else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
        }
        var s = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(s) ? Guid.NewGuid().ToString("N")[..8] : s;
    }
}
