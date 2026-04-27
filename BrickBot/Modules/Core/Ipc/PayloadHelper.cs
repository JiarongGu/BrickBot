using System.Text.Json;
using BrickBot.Modules.Core.Exceptions;

namespace BrickBot.Modules.Core.Ipc;

public sealed class PayloadHelper
{
    private readonly JsonSerializerOptions _options;

    public PayloadHelper(JsonSerializerOptions options)
    {
        _options = options;
    }

    public T GetRequiredValue<T>(JsonElement? payload, string key)
    {
        if (payload is null)
        {
            throw new OperationException("PAYLOAD_MISSING", new() { ["key"] = key });
        }

        if (!payload.Value.TryGetProperty(key, out var prop))
        {
            throw new OperationException("PAYLOAD_FIELD_MISSING", new() { ["key"] = key });
        }

        return prop.Deserialize<T>(_options)
            ?? throw new OperationException("PAYLOAD_FIELD_NULL", new() { ["key"] = key });
    }

    public T? GetOptionalValue<T>(JsonElement? payload, string key)
    {
        if (payload is null) return default;
        if (!payload.Value.TryGetProperty(key, out var prop)) return default;
        return prop.Deserialize<T>(_options);
    }
}
