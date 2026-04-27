using Microsoft.Extensions.DependencyInjection;

namespace BrickBot.Modules.Core.Ipc;

/// <summary>
/// Marker registered by each module's ServiceExtensions so the bootstrapper
/// can populate the FacadeRegistry without modules referencing each other.
/// </summary>
public interface IFacadeRegistration
{
    string Module { get; }
    BaseFacade Facade { get; }
}

internal sealed class FacadeRegistration : IFacadeRegistration
{
    public string Module { get; }
    public BaseFacade Facade { get; }

    public FacadeRegistration(string module, BaseFacade facade)
    {
        Module = module;
        Facade = facade;
    }
}

public static class FacadeRegistrationExtensions
{
    public static IServiceCollection AddFacadeRegistration<TFacade>(
        this IServiceCollection services,
        string moduleName)
        where TFacade : BaseFacade
    {
        services.AddSingleton<IFacadeRegistration>(sp =>
            new FacadeRegistration(moduleName, sp.GetRequiredService<TFacade>()));
        return services;
    }
}
