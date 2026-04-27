using BrickBot.Modules.Template.Models;

namespace BrickBot.Modules.Template.Services;

/// <summary>
/// Façade combining template file storage (data/profiles/{id}/templates/{Id}.png) and
/// metadata (Templates table: name, description). Used by the Template IPC facade and by
/// the script host's <c>vision.find('foo')</c> path resolver.
///
/// Naming is now decoupled from storage — the on-disk filename is the row's <c>Id</c> (Guid),
/// and <c>Name</c>/<c>Description</c> live in the database. Users can rename freely without
/// breaking detection references that point at the id.
/// </summary>
public interface ITemplateFileService
{
    /// <summary>List all templates for the profile, ordered by name.</summary>
    Task<IReadOnlyList<TemplateInfo>> ListAsync(string profileId);

    /// <summary>Persist PNG bytes + metadata. Pass an empty <paramref name="id"/> to create a new template;
    /// pass an existing id to overwrite. Returns the saved row.</summary>
    Task<TemplateInfo> SavePngAsync(string profileId, string id, string name, string? description, byte[] pngBytes);

    /// <summary>Update name/description without touching the image.</summary>
    Task<TemplateInfo> UpdateMetadataAsync(string profileId, string id, string name, string? description);

    /// <summary>Delete row + image file.</summary>
    Task DeleteAsync(string profileId, string id);

    /// <summary>Resolve a template lookup token (id OR name) to its absolute PNG path. Returns null
    /// when the row exists but the file is missing on disk; throws when neither row nor file exists.</summary>
    Task<string?> ResolvePathAsync(string profileId, string idOrName);

    /// <summary>Absolute filesystem path for a template id (whether or not it exists).</summary>
    string GetPath(string profileId, string id);
}
