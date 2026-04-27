using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Runner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Runner;

public static class RunnerServiceExtensions
{
    public static IServiceCollection AddRunnerServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IRunLog, RunLog>();
        services.TryAddSingleton<IRunnerService, RunnerService>();
        services.TryAddSingleton<RunnerFacade>();
        services.AddFacadeRegistration<RunnerFacade>(ModuleNames.RUNNER);
        return services;
    }
}
