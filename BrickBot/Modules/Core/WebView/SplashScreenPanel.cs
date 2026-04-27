using System.Drawing;
using BrickBot.Modules.Core.Utilities;

namespace BrickBot.Modules.Core.WebView;

/// <summary>
/// Splash screen panel overlay shown while WebView2 compiles JavaScript.
/// Theme-aware indeterminate progress bar centered on the form.
/// </summary>
public sealed class SplashScreenPanel : Panel
{
    private readonly ProgressBar _progressBar;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Panel _contentPanel;
    private bool _isDarkTheme;
    private readonly WinFormsDebounce _resizeDebounce;

    public SplashScreenPanel(bool isDarkTheme = false)
    {
        _isDarkTheme = isDarkTheme;

        Dock = DockStyle.Fill;
        BackColor = _isDarkTheme ? Color.FromArgb(31, 31, 31) : Color.FromArgb(230, 244, 255);
        DoubleBuffered = true;

        _contentPanel = new Panel
        {
            Size = new Size(400, 4),
            BackColor = Color.Transparent,
        };
        Controls.Add(_contentPanel);

        _titleLabel = new Label { Visible = false };
        _statusLabel = new Label { Visible = false };

        _progressBar = new ProgressBar
        {
            Location = new Point(0, 0),
            Size = new Size(400, 4),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 20,
            ForeColor = _isDarkTheme ? Color.FromArgb(23, 125, 220) : Color.FromArgb(24, 144, 255),
        };
        _contentPanel.Controls.Add(_progressBar);

        _resizeDebounce = new WinFormsDebounce(50);
        Resize += (_, _) => _resizeDebounce.Execute(UpdateProgressBarSize);

        BringToFront();
    }

    private void UpdateProgressBarSize()
    {
        var progressWidth = Math.Min((int)(Width * 0.7), 400);
        _contentPanel.Size = new Size(progressWidth, 4);
        _progressBar.Size = new Size(progressWidth, 4);
        _contentPanel.Location = new Point(
            (Width - _contentPanel.Width) / 2,
            (Height - _contentPanel.Height) / 2);
    }

    public void UpdateStatus(string status)
    {
        // Status text is hidden — kept as a no-op for caller compatibility.
    }

    public void UpdateProgress(int value)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => UpdateProgress(value)));
            return;
        }
        if (_progressBar.Style != ProgressBarStyle.Continuous)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
        }
        _progressBar.Value = Math.Min(value, 100);
    }

    public void SetTheme(bool isDark)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => SetTheme(isDark)));
            return;
        }
        _isDarkTheme = isDark;
        BackColor = _isDarkTheme ? Color.FromArgb(31, 31, 31) : Color.FromArgb(230, 244, 255);
        _progressBar.ForeColor = _isDarkTheme ? Color.FromArgb(23, 125, 220) : Color.FromArgb(24, 144, 255);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resizeDebounce.Dispose();
        }
        base.Dispose(disposing);
    }
}
