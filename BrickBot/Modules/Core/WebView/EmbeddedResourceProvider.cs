using System.Reflection;

namespace BrickBot.Modules.Core.WebView;

/// <summary>
/// Resolves the on-disk path WebView2 should serve from.
/// In Debug we serve from the source `wwwroot` folder for a fast inner loop.
/// In published single-file builds we extract embedded resources to a temp folder once.
/// </summary>
public interface IEmbeddedResourceProvider
{
    string RootPath { get; }
}

public sealed class EmbeddedResourceProvider : IEmbeddedResourceProvider
{
    private const string ResourcePrefix = "BrickBot.wwwroot.";

    public string RootPath { get; }

    public EmbeddedResourceProvider()
    {
        RootPath = ResolveRoot();
    }

    private static string ResolveRoot()
    {
        var localWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(localWwwroot) && Directory.EnumerateFiles(localWwwroot).Any())
        {
            return localWwwroot;
        }

        var extractDir = Path.Combine(Path.GetTempPath(), "BrickBot", "wwwroot");
        Directory.CreateDirectory(extractDir);
        ExtractEmbeddedResources(extractDir);
        return extractDir;
    }

    private static void ExtractEmbeddedResources(string targetDir)
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;

            var relative = name[ResourcePrefix.Length..].Replace('.', Path.DirectorySeparatorChar);
            var lastSep = relative.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastSep > 0)
            {
                var fileName = relative[(lastSep + 1)..];
                var folder = relative[..lastSep];
                relative = Path.Combine(folder, fileName);
            }
            var outputPath = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var src = assembly.GetManifestResourceStream(name);
            if (src is null) continue;
            using var dest = File.Create(outputPath);
            src.CopyTo(dest);
        }
    }
}
