using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Profile.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Profile;

public static class ProfileServiceExtensions
{
    public static IServiceCollection AddProfileServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IGlobalPathService, GlobalPathService>();
        services.TryAddSingleton<IProfileRepository, ProfileRepository>();
        services.TryAddSingleton<IProfileService, ProfileService>();
        services.TryAddSingleton<IProfileTempService, ProfileTempService>();

        services.TryAddSingleton<ProfileFacade>();
        services.AddFacadeRegistration<ProfileFacade>(ModuleNames.PROFILE);
        return services;
    }
}
