using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Setting.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Setting;

public static class SettingServiceExtensions
{
    public static IServiceCollection AddSettingServices(this IServiceCollection services)
    {
        // Path service belongs to Core conceptually but is registered here so a fresh
        // BrickBot host that only adds Setting still has it. TryAdd makes it idempotent.
        services.TryAddSingleton<IGlobalPathService, GlobalPathService>();
        services.AddMemoryCache();

        services.TryAddSingleton<IGlobalSettingService, GlobalSettingService>();
        services.TryAddSingleton<ISettingFileService, SettingFileService>();
        services.TryAddSingleton<ILanguageService, LanguageService>();
        services.TryAddSingleton<IWindowStateService, WindowStateService>();

        services.TryAddSingleton<SettingFacade>();
        services.AddFacadeRegistration<SettingFacade>(ModuleNames.SETTING);

        return services;
    }
}
