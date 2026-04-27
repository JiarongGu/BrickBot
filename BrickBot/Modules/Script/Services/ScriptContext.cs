using System.Collections.Concurrent;

namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Per-Run shared key/value store, exposed to scripts as the <c>ctx</c> global.
/// Library scripts (e.g. perception/monitor helpers) write status here; the main script reads
/// to drive decisions. Values are stored as JSON strings so any JSON-serializable JS value works
/// (numbers, strings, booleans, plain objects, arrays) without type-conversion hassles between
/// Jint and the C# host.
///
/// Thread-safe. A new instance is created at the start of every Run and discarded when the Run ends.
/// </summary>
public sealed class ScriptContext
{
    private readonly ConcurrentDictionary<string, string> _state = new();

    /// <summary>Write a JSON-encoded value for <paramref name="key"/>. Caller is responsible for the encoding.</summary>
    public void setJson(string key, string json) => _state[key] = json ?? "null";

    /// <summary>Returns the stored JSON string, or <c>null</c> if the key is absent.</summary>
    public string? getJson(string key) => _state.TryGetValue(key, out var v) ? v : null;

    public bool has(string key) => _state.ContainsKey(key);

    public bool delete(string key) => _state.TryRemove(key, out _);

    public string[] keys() => _state.Keys.ToArray();

    public void clear() => _state.Clear();
}
