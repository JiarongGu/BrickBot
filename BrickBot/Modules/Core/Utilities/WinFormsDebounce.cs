namespace BrickBot.Modules.Core.Utilities;

/// <summary>
/// WinForms-compatible debounce utility using System.Windows.Forms.Timer.
/// Delays action execution until a specified time has passed without new calls.
/// Safe for UI thread operations.
/// </summary>
public sealed class WinFormsDebounce : IDisposable
{
    private readonly global::System.Windows.Forms.Timer _timer;
    private Action? _pendingAction;
    private readonly object _lock = new();
    private bool _isDisposed;

    public WinFormsDebounce(int delayMs)
    {
        _timer = new global::System.Windows.Forms.Timer { Interval = delayMs };
        _timer.Tick += OnTimerTick;
    }

    public void Execute(Action action)
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _pendingAction = action;
            _timer.Stop();
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Action? actionToExecute;
        lock (_lock)
        {
            _timer.Stop();
            actionToExecute = _pendingAction;
            _pendingAction = null;
        }
        actionToExecute?.Invoke();
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _timer.Stop();
            _pendingAction = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _timer.Stop();
            _timer.Dispose();
            _pendingAction = null;
        }
    }
}
