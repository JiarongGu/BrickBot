using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Detection.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Detection;

public static class DetectionServiceExtensions
{
    public static IServiceCollection AddDetectionServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IDetectionFileService, DetectionFileService>();
        services.TryAddSingleton<IDetectionModelStore, DetectionModelStore>();
        services.TryAddSingleton<IDetectionRunner, DetectionRunner>();
        services.TryAddSingleton<IDetectionTrainerService, DetectionTrainerService>();
        services.TryAddSingleton<ITrainingSampleRepository, TrainingSampleRepository>();
        services.TryAddSingleton<ITrainingSampleService, TrainingSampleService>();
        services.TryAddSingleton<DetectionFacade>();
        services.AddFacadeRegistration<DetectionFacade>(ModuleNames.DETECTION);
        return services;
    }
}
