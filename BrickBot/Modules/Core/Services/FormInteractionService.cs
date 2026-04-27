namespace BrickBot.Modules.Core.Services;

/// <summary>
/// Tracks the main form so dialogs can use it as their owner and so blocking modal
/// operations can disable input across the app.
/// </summary>
public interface IFormInteractionService
{
    void SetMainForm(Form form);
    Form? GetMainForm();
    IntPtr GetMainFormHandle();
    void BlockInteraction();
    void UnblockInteraction();
}

/// <summary>
/// Tracks the main form and supports nested block/unblock pairs by ref-counting.
/// Marshals to the UI thread when called from a worker.
/// </summary>
public sealed class FormInteractionService : IFormInteractionService
{
    private Form? _mainForm;
    private int _blockCount;
    private readonly object _lock = new();

    public void SetMainForm(Form form)
    {
        _mainForm = form ?? throw new ArgumentNullException(nameof(form));
    }

    public Form? GetMainForm() => _mainForm;

    public IntPtr GetMainFormHandle()
    {
        if (_mainForm == null) return IntPtr.Zero;

        if (_mainForm.InvokeRequired)
        {
            return (IntPtr)_mainForm.Invoke(() => _mainForm.Handle);
        }
        return _mainForm.Handle;
    }

    public void BlockInteraction()
    {
        if (_mainForm == null) return;

        lock (_lock)
        {
            _blockCount++;
            if (_blockCount == 1)
            {
                if (_mainForm.InvokeRequired)
                    _mainForm.Invoke(() => _mainForm.Enabled = false);
                else
                    _mainForm.Enabled = false;
            }
        }
    }

    public void UnblockInteraction()
    {
        if (_mainForm == null) return;

        lock (_lock)
        {
            _blockCount = Math.Max(0, _blockCount - 1);
            if (_blockCount == 0)
            {
                if (_mainForm.InvokeRequired)
                    _mainForm.Invoke(() => _mainForm.Enabled = true);
                else
                    _mainForm.Enabled = true;
            }
        }
    }
}
