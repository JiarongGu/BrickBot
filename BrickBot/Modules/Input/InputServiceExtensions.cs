using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Input.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Input;

public static class InputServiceExtensions
{
    public static IServiceCollection AddInputServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IInputService, SendInputService>();
        services.TryAddSingleton<InputFacade>();
        services.AddFacadeRegistration<InputFacade>(ModuleNames.INPUT);
        return services;
    }
}
