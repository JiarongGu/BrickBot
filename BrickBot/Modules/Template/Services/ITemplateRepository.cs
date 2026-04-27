using BrickBot.Modules.Template.Entities;

namespace BrickBot.Modules.Template.Services;

/// <summary>
/// Per-profile template metadata store. Image bytes live on disk at
/// <c>data/profiles/{profileId}/templates/{Id}.png</c>; this repository owns the metadata
/// (id, name, description, dimensions, timestamps) backed by the profile SQLite database.
/// </summary>
public interface ITemplateRepository
{
    Task<List<TemplateEntity>> ListAsync(string profileId);
    Task<TemplateEntity?> GetByIdAsync(string profileId, string id);
    Task<TemplateEntity?> GetByNameAsync(string profileId, string name);
    Task UpsertAsync(string profileId, TemplateEntity entity);
    Task<bool> DeleteAsync(string profileId, string id);
}
