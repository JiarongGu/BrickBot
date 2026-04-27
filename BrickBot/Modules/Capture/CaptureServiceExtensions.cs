using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Capture;

public static class CaptureServiceExtensions
{
    public static IServiceCollection AddCaptureServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IWindowFinder, WindowFinder>();
        // BitBltCaptureService is registered as a concrete fallback so WinRtCaptureService
        // can inject it directly. ICaptureService is the WinRT-primary path that owns the
        // fallback decision (see WinRtCaptureService for the routing rules).
        services.TryAddSingleton<BitBltCaptureService>();
        services.TryAddSingleton<ICaptureService, WinRtCaptureService>();
        services.TryAddSingleton<IFrameBuffer, FrameBuffer>();
        services.TryAddSingleton<IScreenshotService, ScreenshotService>();
        services.TryAddSingleton<CaptureFacade>();
        services.AddFacadeRegistration<CaptureFacade>(ModuleNames.CAPTURE);
        return services;
    }
}
