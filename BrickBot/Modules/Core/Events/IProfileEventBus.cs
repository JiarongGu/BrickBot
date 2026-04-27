namespace BrickBot.Modules.Core.Events;

public interface IProfileEventBus
{
    Task EmitAsync(string module, string type, object? payload = null);
    void Subscribe(Func<EventEnvelope, Task> handler);
}

public sealed record EventEnvelope(string Module, string Type, object? Payload);
