using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Vision.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Vision;

public static class VisionServiceExtensions
{
    public static IServiceCollection AddVisionServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IVisionService, VisionService>();
        services.TryAddSingleton<ITemplateLoader, TemplateLoader>();
        services.TryAddSingleton<VisionFacade>();
        services.AddFacadeRegistration<VisionFacade>(ModuleNames.VISION);
        return services;
    }
}
