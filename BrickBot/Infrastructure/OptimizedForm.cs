namespace BrickBot.Infrastructure;

/// <summary>
/// Base WinForms form with double-buffered, flicker-free rendering for hosting WebView2.
/// Subclasses (e.g. <see cref="MainForm"/>) add the actual layout and event wiring.
/// </summary>
public class OptimizedForm : Form
{
    public OptimizedForm()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.DoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        UpdateStyles();

        // Allow drag-and-drop so future drop-zone overlays can intercept file drags.
        AllowDrop = true;

        DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
    }
}
