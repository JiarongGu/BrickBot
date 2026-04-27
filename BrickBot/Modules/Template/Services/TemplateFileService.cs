using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Services;

namespace BrickBot.Modules.Template.Services;

public sealed class TemplateFileService : ITemplateFileService
{
    private readonly IGlobalPathService _globalPaths;
    private readonly ILogHelper _logger;

    public TemplateFileService(IGlobalPathService globalPaths, ILogHelper logger)
    {
        _globalPaths = globalPaths;
        _logger = logger;
    }

    public IReadOnlyList<string> List(string profileId)
    {
        var dir = _globalPaths.GetProfileTemplatesDirectory(profileId);
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        return Directory.EnumerateFiles(dir, "*.png")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToList();
    }

    public string SavePng(string profileId, string name, byte[] pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            throw new OperationException("TEMPLATE_EMPTY_PAYLOAD");
        }
        var path = GetPath(profileId, name);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, pngBytes);
        _logger.Info($"Saved template {name}.png ({pngBytes.Length} bytes) for profile {profileId}", "Template");
        return path;
    }

    public void Delete(string profileId, string name)
    {
        var path = GetPath(profileId, name);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.Info($"Deleted template {name}.png for profile {profileId}", "Template");
        }
    }

    public string GetPath(string profileId, string name)
    {
        ValidateName(name);
        var fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.png";
        return Path.Combine(_globalPaths.GetProfileTemplatesDirectory(profileId), fileName);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new OperationException("TEMPLATE_NAME_REQUIRED");

        var invalid = Path.GetInvalidFileNameChars();
        if (name.Any(c => invalid.Contains(c)) ||
            name.Contains("..") || name.Contains('/') || name.Contains('\\'))
        {
            throw new OperationException("TEMPLATE_INVALID_NAME", new() { ["name"] = name });
        }
    }
}
