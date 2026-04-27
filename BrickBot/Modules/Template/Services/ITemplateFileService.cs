namespace BrickBot.Modules.Template.Services;

/// <summary>
/// CRUD over the per-profile templates directory at <c>data/profiles/{id}/templates/</c>.
/// Used by the Capture &amp; Templates UI to save cropped regions as PNGs that scripts can
/// reference via <c>vision.find('name.png')</c>.
/// </summary>
public interface ITemplateFileService
{
    /// <summary>List PNG file names (no extension) for a profile.</summary>
    IReadOnlyList<string> List(string profileId);

    /// <summary>Save raw PNG bytes as <c>name</c>.png. Overwrites if it exists. Returns the absolute path.</summary>
    string SavePng(string profileId, string name, byte[] pngBytes);

    /// <summary>Delete <c>name</c>.png. No-op if missing.</summary>
    void Delete(string profileId, string name);

    /// <summary>Returns the absolute filesystem path for a template (whether or not it exists).</summary>
    string GetPath(string profileId, string name);
}
