using OpenCvSharp;

namespace BrickBot.Modules.Vision.Services;

/// <summary>
/// Loads + caches template Mats from absolute file paths. Templates are reused
/// across many script ticks so caching is important.
/// </summary>
public interface ITemplateLoader
{
    Mat Load(string path);
    void Invalidate(string path);
}

public sealed class TemplateLoader : ITemplateLoader, IDisposable
{
    private readonly Dictionary<string, Mat> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public Mat Load(string path)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var mat = Cv2.ImRead(path, ImreadModes.Color);
            if (mat.Empty()) throw new FileNotFoundException($"Template not found or unreadable: {path}");
            _cache[path] = mat;
            return mat;
        }
    }

    public void Invalidate(string path)
    {
        lock (_lock)
        {
            if (_cache.Remove(path, out var mat)) mat.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var mat in _cache.Values) mat.Dispose();
            _cache.Clear();
        }
    }
}
