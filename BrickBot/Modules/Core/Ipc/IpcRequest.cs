using System.Text.Json;

namespace BrickBot.Modules.Core.Ipc;

public sealed class IpcRequest
{
    public string Id { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? ProfileId { get; init; }
    public JsonElement? Payload { get; init; }
}

public sealed class IpcResponse
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = "IPC";
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public ErrorDetails? ErrorDetails { get; init; }
}

public sealed record ErrorDetails(string Code, Dictionary<string, string>? Parameters);
