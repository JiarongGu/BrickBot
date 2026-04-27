using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Template.Entities;
using BrickBot.Modules.Template.Models;
using OpenCvSharp;

namespace BrickBot.Modules.Template.Services;

public sealed class TemplateFileService : ITemplateFileService
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ITemplateRepository _repository;
    private readonly ILogHelper _logger;

    public TemplateFileService(
        IGlobalPathService globalPaths,
        ITemplateRepository repository,
        ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _repository = repository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TemplateInfo>> ListAsync(string profileId)
    {
        var rows = await _repository.ListAsync(profileId).ConfigureAwait(false);
        return rows.Select(ToInfo).ToList();
    }

    public async Task<TemplateInfo> SavePngAsync(
        string profileId, string id, string name, string? description, byte[] pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            throw new OperationException("TEMPLATE_EMPTY_PAYLOAD");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new OperationException("TEMPLATE_NAME_REQUIRED");
        }

        var entity = string.IsNullOrEmpty(id)
            ? new TemplateEntity { Id = Guid.NewGuid().ToString("N") }
            : (await _repository.GetByIdAsync(profileId, id).ConfigureAwait(false))
              ?? new TemplateEntity { Id = id };

        var path = GetPath(profileId, entity.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, pngBytes).ConfigureAwait(false);

        // Decode for dimensions — keeps the row metadata accurate so the UI can show size
        // without re-reading the file. Mat.Empty defends against truncated/garbage payloads.
        using var mat = Cv2.ImDecode(pngBytes, ImreadModes.Color);
        if (mat.Empty()) throw new OperationException("TEMPLATE_DECODE_FAILED", new() { ["bytes"] = pngBytes.Length.ToString() });

        entity.Name = name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        entity.Width = mat.Width;
        entity.Height = mat.Height;
        await _repository.UpsertAsync(profileId, entity).ConfigureAwait(false);

        _logger.Info($"Saved template {entity.Id} ({entity.Name}) {entity.Width}×{entity.Height}", "Template");
        return ToInfo(entity);
    }

    public async Task<TemplateInfo> UpdateMetadataAsync(string profileId, string id, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new OperationException("TEMPLATE_NAME_REQUIRED");
        var existing = await _repository.GetByIdAsync(profileId, id).ConfigureAwait(false)
            ?? throw new OperationException("TEMPLATE_NOT_FOUND", new() { ["id"] = id });
        existing.Name = name.Trim();
        existing.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await _repository.UpsertAsync(profileId, existing).ConfigureAwait(false);
        return ToInfo(existing);
    }

    public async Task DeleteAsync(string profileId, string id)
    {
        var path = GetPath(profileId, id);
        await _repository.DeleteAsync(profileId, id).ConfigureAwait(false);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.Info($"Deleted template {id}", "Template");
        }
    }

    public async Task<string?> ResolvePathAsync(string profileId, string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;

        // Strip a trailing .png so legacy `vision.find('hp_bar.png')` still resolves.
        var token = idOrName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? idOrName[..^4]
            : idOrName;

        // Try id first (unique), then fall back to name lookup. First name match wins —
        // user-friendly names are not unique by design but most are.
        var entity = await _repository.GetByIdAsync(profileId, token).ConfigureAwait(false)
                     ?? await _repository.GetByNameAsync(profileId, token).ConfigureAwait(false);
        if (entity is null) return null;

        var path = GetPath(profileId, entity.Id);
        return File.Exists(path) ? path : null;
    }

    public string GetPath(string profileId, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new OperationException("TEMPLATE_ID_REQUIRED");
        // No path-traversal concern: id is a Guid string; we still validate to stay safe.
        if (id.Contains('/') || id.Contains('\\') || id.Contains(".."))
        {
            throw new OperationException("TEMPLATE_INVALID_ID", new() { ["id"] = id });
        }
        return Path.Combine(_globalPaths.GetProfileTemplatesDirectory(profileId), $"{id}.png");
    }

    private static TemplateInfo ToInfo(TemplateEntity e) => new(
        Id: e.Id,
        Name: e.Name,
        Description: e.Description,
        Width: e.Width,
        Height: e.Height,
        CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)));
}
