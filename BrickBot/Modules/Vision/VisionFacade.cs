using BrickBot.Modules.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Vision;

/// <summary>
/// Vision is invoked primarily from inside scripts (not directly from the
/// frontend), so the facade surface is thin for now.
/// </summary>
public sealed class VisionFacade : BaseFacade
{
    public VisionFacade(ILogger<VisionFacade> logger) : base(logger) { }

    protected override Task<object?> RouteMessageAsync(IpcRequest request)
    {
        throw new InvalidOperationException($"VISION has no IPC types yet (got: {request.Type})");
    }
}
