using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Models;

namespace BrickBot.Infrastructure;

/// <summary>
/// Bootstraps the application with proper initialization order.
/// Lifecycle:
///   1. Create AppEnvironment (env detection + log level)
///   2. Create LogHelper (writes to data/logs)
///   3. Initialize WinForms (visual styles, DPI, exception handling)
///   4. Create ApplicationHost
///   5. Initialize services (DI), needed for window-state load
///   6. Create main form (window state already loaded, no visual jump)
///   7. Run the message loop
/// </summary>
public static class ApplicationBootstrapper
{
    private static ILogHelper? _logger;

    public static void Run()
    {
        var appEnv = AppEnvironment.Create(AppDomain.CurrentDomain.BaseDirectory);
        _logger = LogHelper.Create(appEnv);

        _logger.Info("=== BrickBot Starting ===", "Bootstrap");
        _logger.Info($"Environment: {(appEnv.IsDevelopment ? "Development" : "Production")}", "Bootstrap");
        _logger.Info($"Log Level: {appEnv.MinimumLogLevel}", "Bootstrap");
        _logger.Info($"Thread apartment state: {Thread.CurrentThread.GetApartmentState()}", "Bootstrap");

        InitializeWinForms();

        var host = new ApplicationHost(appEnv, _logger);

        // Services first so window-state can load before the form appears.
        host.InitializeServices();

        host.CreateMainForm();

        host.Run();
    }

    private static void InitializeWinForms()
    {
        _logger?.Info("Initializing WinForms...", "Bootstrap");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        _logger?.Info("WinForms initialized", "Bootstrap");
    }
}
