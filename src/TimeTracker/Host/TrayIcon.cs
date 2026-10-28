using System.Drawing;
using System.Windows.Forms;

namespace TimeTracker.Host;

/// <summary>Clock icon in the notification area: left click opens the window, right click — the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _normal = LoadIcon("Assets/app.ico");
    private readonly Icon _paused = LoadIcon("Assets/paused.ico");
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _updateItem;

    public TrayIcon(Action open, Action togglePause, Action restartToUpdate, Action quit)
    {
        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Відкрити", null, (_, _) => open());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        _pauseItem = new ToolStripMenuItem("Пауза", null, (_, _) => togglePause());
        _updateItem = new ToolStripMenuItem("Перезапустити й оновити", null, (_, _) => restartToUpdate()) { Visible = false };
        menu.Items.Add(openItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Вихід", null, (_, _) => quit()));

        _icon = new NotifyIcon { Icon = _normal, Text = "TimeTracker", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) open();
        };
    }

    public void SetPaused(bool paused)
    {
        _icon.Icon = paused ? _paused : _normal;
        _pauseItem.Text = paused ? "Продовжити облік" : "Пауза";
    }

    public void SetUpdateReady(string? version)
    {
        _updateItem.Text = $"Перезапустити й оновити до {version}";
        _updateItem.Visible = true;
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void ShowHint(string title, string text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _normal.Dispose();
        _paused.Dispose();
    }

    private static Icon LoadIcon(string name)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource {name} is missing");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
