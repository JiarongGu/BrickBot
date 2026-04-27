using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Template.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Template;

public static class TemplateServiceExtensions
{
    public static IServiceCollection AddTemplateServices(this IServiceCollection services)
    {
        services.TryAddSingleton<ITemplateFileService, TemplateFileService>();
        services.TryAddSingleton<TemplateFacade>();
        services.AddFacadeRegistration<TemplateFacade>(ModuleNames.TEMPLATE);
        return services;
    }
}
