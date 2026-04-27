using System.Drawing;
using BrickBot.Modules.Capture;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Core.Models;
using BrickBot.Modules.Core.Services;
using BrickBot.Modules.Core.Utilities;
using BrickBot.Modules.Core.WebView;
using BrickBot.Modules.Database;
using BrickBot.Modules.Detection;
using BrickBot.Modules.Input;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Profile;
using BrickBot.Modules.Runner;
using BrickBot.Modules.Script;
using BrickBot.Modules.Setting;
using BrickBot.Modules.Setting.Services;
using BrickBot.Modules.Template;
using BrickBot.Modules.Vision;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrickBot.Infrastructure;

/// <summary>
/// Owns the main form, WebView2, and DI container for the application's lifetime.
/// </summary>
public sealed class ApplicationHost
{
    private readonly IAppEnvironment _environment;
    private ILogHelper _logger;

    private ServiceProvider _serviceProvider = null!;
    private MainForm _mainForm = null!;
    private IEmbeddedResourceProvider _embeddedResources = null!;
    private IpcHandler _ipcHandler = null!;
    private IPerformanceMonitor _performanceMonitor = null!;
    private IFormInteractionService _formInteractionService = null!;
    private IWindowStateService _windowStateService = null!;
    private IEagerLoadingService _eagerLoadingService = null!;

    private WinFormsDebounce? _resizeDebounce;

    public ApplicationHost(IAppEnvironment environment, ILogHelper logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public Form MainForm => _mainForm;

    /// <summary>
    /// Initializes DI services early so window-state can load before the form is created.
    /// Called by <see cref="ApplicationBootstrapper"/> before <see cref="CreateMainForm"/>.
    /// </summary>
    public void InitializeServices()
    {
        _logger.Info("Configuring DI...", "Host");

        var services = new ServiceCollection();

        // Register the bootstrap-phase environment + logger as singletons so DI
        // hands out the same instances we already created.
        services.AddSingleton(_environment);
        services.AddSingleton(_logger);

        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        WireFacadeRegistry();

        // Resolve services we hold for the form lifecycle.
        _embeddedResources = _serviceProvider.GetRequiredService<IEmbeddedResourceProvider>();
        _ipcHandler = _serviceProvider.GetRequiredService<IpcHandler>();
        _performanceMonitor = _serviceProvider.GetRequiredService<IPerformanceMonitor>();
        _formInteractionService = _serviceProvider.GetRequiredService<IFormInteractionService>();
        _windowStateService = _serviceProvider.GetRequiredService<IWindowStateService>();
        _eagerLoadingService = _serviceProvider.GetRequiredService<IEagerLoadingService>();

        _logger.Info("Services initialized", "Host");
    }

    /// <summary>
    /// Creates the main form with window state already loaded so the window appears at its
    /// final size/position with no visual jump.
    /// </summary>
    public void CreateMainForm()
    {
        _logger.Info("Creating main form...", "Host");

        var (width, height, x, y, maximized) = _windowStateService.LoadWindowStateAsync().GetAwaiter().GetResult();

        _mainForm = new MainForm();
        _mainForm.SuspendLayout();

        _mainForm.Width = width;
        _mainForm.Height = height;

        if (x.HasValue && y.HasValue && _windowStateService.IsPositionValid(x.Value, y.Value, width, height, _mainForm))
        {
            _mainForm.Left = x.Value;
            _mainForm.Top = y.Value;
            _logger.Info($"Applied saved window position: ({x.Value}, {y.Value})", "Host");
        }
        else
        {
            CenterFormOnScreen();
            _logger.Info("Centered on screen (no/invalid saved position)", "Host");
        }

        if (maximized)
        {
            _mainForm.WindowState = FormWindowState.Maximized;
        }

        TryLoadIcon();

        // Splash screen (default to dark; theme follows once frontend reports it)
        var splash = new SplashScreenPanel(isDarkTheme: true);
        splash.UpdateStatus("Initializing application...");
        _mainForm.AttachSplashScreen(splash);

        _mainForm.Load += OnFormLoad;
        _mainForm.FormClosed += OnFormClosed;
        _mainForm.Resize += OnFormResize;

        _mainForm.ResumeLayout(false);
        _mainForm.PerformLayout();

        _formInteractionService.SetMainForm(_mainForm);

        _logger.Info("Main form created", "Host");
    }

    public void Run()
    {
        _logger.Info("Starting application message loop...", "Host");
        Application.Run(_mainForm);
        _logger.Info("Application ended", "Host");
    }

    /// <summary>
    /// Configure all DI services. IAppEnvironment + ILogHelper are pre-registered.
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        // Microsoft.Extensions.Logging powers ILogger<T> consumed by existing facades.
        // ILogHelper handles bootup/lifecycle/file logging in parallel.
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(MapLogLevel(_environment.MinimumLogLevel));
            if (_environment.IsDevelopment)
            {
                builder.AddSimpleConsole();
            }
        });

        services
            .AddCoreServices()
            .AddDatabaseServices()
            .AddCaptureServices()
            .AddVisionServices()
            .AddInputServices()
            .AddTemplateServices()
            .AddDetectionServices()
            .AddScriptServices()
            .AddRunnerServices()
            .AddProfileServices()
            .AddSettingServices();
    }

    /// <summary>
    /// Each module's ServiceExtensions registers an <see cref="IFacadeRegistration"/>;
    /// the bootstrapper reads them here and populates the global facade registry.
    /// </summary>
    private void WireFacadeRegistry()
    {
        var registry = _serviceProvider.GetRequiredService<IFacadeRegistry>();
        foreach (var registration in _serviceProvider.GetServices<IFacadeRegistration>())
        {
            registry.Register(registration.Module, registration.Facade);
            _logger.Verbose($"Registered facade: {registration.Module}", "Host");
        }
    }

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        try
        {
            _logger.Info("Form loaded, initializing WebView2...", "Host");
            _performanceMonitor.StartOperation("WebView2.Initialize");

            await PerformEagerLoadingAsync().ConfigureAwait(true);

            // Subscribe to events that affect the host shell (window state reset, etc.).
            // Doing this after services are up but before WebView2 init keeps the.
            SubscribeHostEvents();

            await InitializeWebViewAsync().ConfigureAwait(true);

            _performanceMonitor.StopOperation("WebView2.Initialize");
            _logger.Info("Application initialized", "Host");
        }
        catch (Exception ex)
        {
            _logger.Error($"Initialization error: {ex.Message}", "Host", ex);
            MessageBox.Show(
                $"Failed to initialize application:\n\n{ex.Message}",
                "Initialization Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task PerformEagerLoadingAsync()
    {
        _logger.Info("Starting eager loading...", "Host");

        var splash = _mainForm.SplashScreen;
        var progress = new Progress<EagerLoadingProgress>(p =>
        {
            splash?.UpdateStatus(p.Operation);
            _logger.Verbose($"Eager loading: {p.Operation} ({p.Percent}%)", "Host");
        });

        try
        {
            await _eagerLoadingService.EagerLoadAsync(progress).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Eager loading failed (non-critical): {ex.Message}", "Host");
        }
    }

    private const string DevServerUrl = "http://localhost:3000";
    private const string ProductionUrl = "https://app.local/index.html";

    private async Task InitializeWebViewAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrickBot", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
        await _mainForm.WebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        // Map embedded resources to https://app.local/ for production navigation.
        // In dev we navigate to the Vite dev server instead, but the mapping is harmless.
        _mainForm.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local",
            _embeddedResources.RootPath,
            CoreWebView2HostResourceAccessKind.Allow);

        _ipcHandler.Attach(_mainForm.WebView);

        _mainForm.WebView.NavigationCompleted += (_, _) => _mainForm.RemoveSplashScreen();

        // Dev mode: hit the Vite dev server for HMR.
        // Prod mode: serve the embedded React build from app.local.
        var targetUrl = _environment.IsDevelopment ? DevServerUrl : ProductionUrl;

        if (_environment.IsDevelopment)
        {
            _logger.Info($"Development mode — navigating to Vite dev server at {DevServerUrl}", "Host");
            _logger.Info("If the page fails to load, run `npm run start` in BrickBot.Client/", "Host");
        }
        else
        {
            _logger.Info($"Production mode — navigating to embedded build at {ProductionUrl}", "Host");
        }

        _mainForm.WebView.Source = new Uri(targetUrl);
    }

    /// <summary>
    /// Subscribe ApplicationHost to events that need to mutate the main form
    /// (e.g. WINDOW_STATE_RESET from the Settings UI).
    /// OnFormLoad event subscription.
    /// </summary>
    private void SubscribeHostEvents()
    {
        var eventBus = _serviceProvider.GetRequiredService<IProfileEventBus>();
        eventBus.Subscribe(envelope =>
        {
            if (envelope.Module == ModuleNames.SETTING && envelope.Type == SettingEvents.WINDOW_STATE_RESET)
            {
                return HandleWindowStateResetAsync(envelope);
            }
            return Task.CompletedTask;
        });
        _logger.Info("Host event subscriptions wired", "Host");
    }

    private Task HandleWindowStateResetAsync(EventEnvelope envelope)
    {
        try
        {
            // Payload from SettingFacade.ResetWindowStateAsync: { width, height, maximized }.
            // Marshal to UI thread before touching the form.
            int width = 1280;
            int height = 800;

            if (envelope.Payload is not null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(envelope.Payload);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("width", out var w) && w.TryGetInt32(out var wInt)) width = wInt;
                if (doc.RootElement.TryGetProperty("height", out var h) && h.TryGetInt32(out var hInt)) height = hInt;
            }

            _logger.Info($"Applying window state reset: {width}x{height}", "Host");

            void Apply()
            {
                if (_mainForm.WindowState == FormWindowState.Maximized)
                {
                    _mainForm.WindowState = FormWindowState.Normal;
                }
                _mainForm.Width = width;
                _mainForm.Height = height;
                CenterFormOnScreen();
            }

            if (_mainForm.InvokeRequired) _mainForm.Invoke(Apply);
            else Apply();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to apply window state reset: {ex.Message}", "Host", ex);
        }

        return Task.CompletedTask;
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        _resizeDebounce ??= new WinFormsDebounce(50);
        _resizeDebounce.Execute(() =>
        {
            if (_mainForm.WebView?.IsHandleCreated == true && _mainForm.WindowState != FormWindowState.Minimized)
            {
                _mainForm.WebView.SuspendLayout();
                _mainForm.WebView.ResumeLayout(false);
            }
        });
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _logger.Info("Form closed, cleaning up...", "Host");

        try
        {
            try { _windowStateService.SaveWindowStateAsync(_mainForm).Wait(); }
            catch (Exception ex) { _logger.Error($"Failed to save window state: {ex.Message}", "Host", ex); }

            _resizeDebounce?.Dispose();

            _serviceProvider.Dispose();

            _logger.Info("Cleanup completed", "Host");
        }
        catch (Exception ex)
        {
            _logger?.Error($"Error during cleanup: {ex.Message}", "Host", ex);
        }
    }

    private void CenterFormOnScreen()
    {
        var screen = Screen.PrimaryScreen;
        if (screen is null) return;
        var area = screen.WorkingArea;
        _mainForm.Left = area.Left + (area.Width - _mainForm.Width) / 2;
        _mainForm.Top = area.Top + (area.Height - _mainForm.Height) / 2;
    }

    private static Microsoft.Extensions.Logging.LogLevel MapLogLevel(BrickBot.Modules.Core.Helpers.LogLevel level) => level switch
    {
        BrickBot.Modules.Core.Helpers.LogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace,
        BrickBot.Modules.Core.Helpers.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        BrickBot.Modules.Core.Helpers.LogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
        BrickBot.Modules.Core.Helpers.LogLevel.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
        BrickBot.Modules.Core.Helpers.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        BrickBot.Modules.Core.Helpers.LogLevel.Off => Microsoft.Extensions.Logging.LogLevel.None,
        BrickBot.Modules.Core.Helpers.LogLevel.All => Microsoft.Extensions.Logging.LogLevel.Trace,
        _ => Microsoft.Extensions.Logging.LogLevel.Information,
    };

    private void TryLoadIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream("BrickBot.favicon.ico");
            if (iconStream != null)
            {
                _mainForm.Icon = new Icon(iconStream);
                _logger.Info("Window icon loaded from embedded resource", "Host");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load window icon: {ex.Message}", "Host");
        }
    }
}
