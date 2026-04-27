using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Recording.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Recording;

public static class RecordingServiceExtensions
{
    public static IServiceCollection AddRecordingServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IRecordingRepository, RecordingRepository>();
        services.TryAddSingleton<IRecordingService, RecordingService>();
        services.TryAddSingleton<RecordingFacade>();
        services.AddFacadeRegistration<RecordingFacade>(ModuleNames.RECORDING);
        return services;
    }
}
