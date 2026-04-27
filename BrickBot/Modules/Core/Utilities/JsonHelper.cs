using System.Text.Json;

namespace BrickBot.Modules.Core.Utilities;

/// <summary>
/// File-based JSON read/write helpers with atomic writes (write-temp-then-move).
/// Uses camelCase + indented output to match the IPC JSON conventions.
///
/// Both methods route their I/O through <see cref="Task.Run(Func{Task})"/> so the
/// async work executes on the threadpool with no captured <see cref="SynchronizationContext"/>.
/// This is critical: the implicit <c>await stream.DisposeAsync()</c> emitted by
/// <c>await using</c> blocks does NOT carry <c>ConfigureAwait(false)</c>, so if the caller
/// happened to be on the WinForms UI thread (e.g. <c>OnFormClosed</c> waiting on us),
/// the dispose continuation would deadlock waiting for that same UI thread to free up.
/// </summary>
public static class JsonHelper
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static Task<T?> DeserializeFromFileAsync<T>(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return Task.FromResult<T?>(default);

        return Task.Run(async () =>
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, DefaultOptions, ct).ConfigureAwait(false);
        }, ct);
    }

    public static Task SerializeToFileAsync<T>(string path, T value, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, DefaultOptions, ct).ConfigureAwait(false);
            }

            // Atomic replace
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }, ct);
    }
}
