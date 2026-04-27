using System.Drawing;
using BrickBot.Modules.Core.WebView;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrickBot.Infrastructure;

/// <summary>
/// Main application window: hosts WebView2 with the embedded React frontend
/// and overlays a splash screen until the WebView is ready.
/// </summary>
public sealed class MainForm : OptimizedForm
{
    private readonly WebView2 _webView;
    private SplashScreenPanel? _splashScreen;

    public MainForm()
    {
        Text = "BrickBot";
        BackColor = Color.FromArgb(26, 26, 26); // match WebView2 background to avoid white flash
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.Manual;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(26, 26, 26),
        };
        Controls.Add(_webView);
    }

    public WebView2 WebView => _webView;

    public void AttachSplashScreen(SplashScreenPanel splash)
    {
        _splashScreen = splash;
        Controls.Add(splash);
        splash.BringToFront();
    }

    public SplashScreenPanel? SplashScreen => _splashScreen;

    public void RemoveSplashScreen()
    {
        if (_splashScreen is null) return;
        if (InvokeRequired)
        {
            Invoke(new Action(RemoveSplashScreen));
            return;
        }
        Controls.Remove(_splashScreen);
        _splashScreen.Dispose();
        _splashScreen = null;
    }
}
