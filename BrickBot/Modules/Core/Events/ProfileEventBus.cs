using System.Collections.Concurrent;

namespace BrickBot.Modules.Core.Events;

public sealed class ProfileEventBus : IProfileEventBus
{
    private readonly ConcurrentBag<Func<EventEnvelope, Task>> _handlers = new();

    public async Task EmitAsync(string module, string type, object? payload = null)
    {
        var envelope = new EventEnvelope(module, type, payload);
        foreach (var handler in _handlers)
        {
            await handler(envelope).ConfigureAwait(false);
        }
    }

    public void Subscribe(Func<EventEnvelope, Task> handler)
    {
        _handlers.Add(handler);
    }
}
