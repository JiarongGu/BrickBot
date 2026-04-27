using System.Text.Json;
using System.Text.Json.Serialization;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Core.Models;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Core.Utilities;
using BrickBot.Modules.Core.WebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrickBot.Modules.Core;

public static class CoreServiceExtensions
{
    /// <summary>
    /// Registers Core module services: events, IPC, JSON, environment, logging,
    /// embedded resources, performance monitoring, eager loading, form interaction.
    /// IAppEnvironment must already be registered as a singleton before this is called
    /// (the bootstrapper creates it eagerly so it's available during ConfigureServices).
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Events + IPC + facade registry
        services.TryAddSingleton<IProfileEventBus, ProfileEventBus>();
        services.TryAddSingleton<IFacadeRegistry, FacadeRegistry>();

        // JSON: camelCase + camelCase enum names (mirrors frontend expectations).
        // IntPtr is serialized as Int64 so Win32 handles round-trip cleanly.
        services.TryAddSingleton(_ => new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new IntPtrJsonConverter(),
            },
        });

        services.TryAddSingleton<PayloadHelper>();
        services.TryAddSingleton<IpcHandler>();

        // Logging (depends on IAppEnvironment registered by the bootstrapper)
        services.TryAddSingleton<ILogHelper, LogHelper>();

        // WebView resources
        services.TryAddSingleton<IEmbeddedResourceProvider, EmbeddedResourceProvider>();

        // Lifecycle services
        services.TryAddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        services.TryAddSingleton<IFormInteractionService, FormInteractionService>();
        services.TryAddSingleton<IEagerLoadingService, EagerLoadingService>();

        return services;
    }
}
