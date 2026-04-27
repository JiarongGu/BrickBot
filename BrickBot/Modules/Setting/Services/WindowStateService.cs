using BrickBot.Modules.Core.Helpers;
using BrickBot.Modules.Setting.Models;

namespace BrickBot.Modules.Setting.Services;

/// <summary>
/// Persists and restores window size/position by writing to <see cref="WindowSettings"/>
/// inside <see cref="Models.GlobalSettings"/> (data/settings/global.json) —.
/// </summary>
public interface IWindowStateService
{
    Task<(int width, int height, int? x, int? y, bool maximized)> LoadWindowStateAsync();
    Task SaveWindowStateAsync(Form form);
    bool IsPositionValid(int x, int y, int width, int height, Form form);
}

public sealed class WindowStateService : IWindowStateService
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 800;
    private const int MinWidth = 800;
    private const int MinHeight = 600;
    private const int MinVisibleWidth = 100;
    private const int MinVisibleHeight = 50;

    private readonly IGlobalSettingService _globalSettings;
    private readonly ILogHelper _logger;

    public WindowStateService(IGlobalSettingService globalSettings, ILogHelper logger)
    {
        _globalSettings = globalSettings;
        _logger = logger;
    }

    public async Task<(int width, int height, int? x, int? y, bool maximized)> LoadWindowStateAsync()
    {
        try
        {
            var settings = await _globalSettings.GetSettingsAsync().ConfigureAwait(false);
            var window = settings.Window;

            var width = Math.Max(window.Width ?? DefaultWidth, MinWidth);
            var height = Math.Max(window.Height ?? DefaultHeight, MinHeight);

            _logger.Info(
                $"Loaded: {width}x{height}, " +
                $"Position: {(window.X.HasValue ? $"X={window.X},Y={window.Y}" : "default")}, " +
                $"Maximized: {window.Maximized}",
                "WindowState");

            return (width, height, window.X, window.Y, window.Maximized);
        }
        catch (Exception ex)
        {
            _logger.Error($"Error loading state: {ex.Message}", "WindowState", ex);
            return (DefaultWidth, DefaultHeight, null, null, false);
        }
    }

    public async Task SaveWindowStateAsync(Form form)
    {
        if (form is null)
        {
            _logger.Warn("Cannot save - form is null", "WindowState");
            return;
        }

        // Capture geometry NOW, before any `await`. Reading form.Left/Top/Width/Height
        // after a thread switch is a cross-thread access and returns garbage (often 0),
        // which would then trip the `width > 0 && height > 0` guard and silently skip the save.
        var maximized = form.WindowState == FormWindowState.Maximized;
        var left = form.Left;
        var top = form.Top;
        var width = form.Width;
        var height = form.Height;

        try
        {
            var settings = await _globalSettings.GetSettingsAsync().ConfigureAwait(false);

            settings.Window.Maximized = maximized;

            if (!maximized && width > 0 && height > 0)
            {
                settings.Window.X = left;
                settings.Window.Y = top;
                settings.Window.Width = width;
                settings.Window.Height = height;
            }

            // Use the window-only update so we don't fire GLOBAL_SETTINGS_CHANGED — this
            // method is called from OnFormClosed where the UI thread is waiting on us
            // synchronously, and an event re-entering the UI thread would deadlock.
            await _globalSettings.UpdateWindowSettingsAsync(settings.Window).ConfigureAwait(false);

            _logger.Info(
                $"Saved: {settings.Window.Width}x{settings.Window.Height}, " +
                $"Position: X={settings.Window.X},Y={settings.Window.Y}, Maximized: {settings.Window.Maximized}",
                "WindowState");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error saving state: {ex.Message}", "WindowState", ex);
        }
    }

    public bool IsPositionValid(int x, int y, int width, int height, Form form)
    {
        if (form is null) return false;

        try
        {
            var screens = Screen.AllScreens;
            if (screens is null || screens.Length == 0)
            {
                _logger.Warn("No screens found", "WindowState");
                return false;
            }

            var windowRight = x + width;
            var windowBottom = y + height;

            foreach (var screen in screens)
            {
                var bounds = screen.Bounds;
                var screenRight = bounds.X + bounds.Width;
                var screenBottom = bounds.Y + bounds.Height;

                var hasOverlap = !(windowRight < bounds.X || x > screenRight || windowBottom < bounds.Y || y > screenBottom);
                if (!hasOverlap) continue;

                var visibleWidth = Math.Min(windowRight, screenRight) - Math.Max(x, bounds.X);
                var visibleHeight = Math.Min(windowBottom, screenBottom) - Math.Max(y, bounds.Y);

                if (visibleWidth >= MinVisibleWidth && visibleHeight >= MinVisibleHeight) return true;
            }

            _logger.Warn($"Position ({x},{y}) not visible on any screen", "WindowState");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error validating position: {ex.Message}", "WindowState", ex);
            return false;
        }
    }
}
