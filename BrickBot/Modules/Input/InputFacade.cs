using BrickBot.Modules.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Input;

/// <summary>
/// Input is invoked from inside scripts, not directly from the frontend.
/// Facade is a no-op stub for now.
/// </summary>
public sealed class InputFacade : BaseFacade
{
    public InputFacade(ILogger<InputFacade> logger) : base(logger) { }

    protected override Task<object?> RouteMessageAsync(IpcRequest request)
    {
        throw new InvalidOperationException($"INPUT has no IPC types yet (got: {request.Type})");
    }
}
