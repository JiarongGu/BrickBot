using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Script.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Script;

public static class ScriptServiceExtensions
{
    public static IServiceCollection AddScriptServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IScriptEngine, JintScriptEngine>();
        services.TryAddSingleton<IScriptFileService, ScriptFileService>();
        services.TryAddSingleton<ScriptFacade>();
        services.AddFacadeRegistration<ScriptFacade>(ModuleNames.SCRIPT);
        return services;
    }
}
