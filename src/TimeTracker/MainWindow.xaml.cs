using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using TimeTracker.Core;
using TimeTracker.Host;

namespace TimeTracker;

public partial class MainWindow : Window
{
    // The page is served from embedded resources under this made-up origin.
    private const string Origin = "https://timetracker.local/";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly App _app;
    private bool _webReady;
    private bool _allowClose;

    internal MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        ApplyTheme(IsDark(app.Settings.Theme));

        Loaded += async (_, _) => await InitWebViewAsync();
        Closing += OnClosing;
        // The page refreshes its data whenever the window comes back from the tray.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) PushEvent("shown", null);
        };
    }

    internal void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
        _app.OnWindowHidden();
    }

    private async Task InitWebViewAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebViewData);
        await Web.EnsureCoreWebView2Async(env);
        var core = Web.CoreWebView2;

        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // no F5 / Ctrl+P; clipboard keys still work
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
        // Right-click menu only where it is useful: copy/paste in text fields.
        core.ContextMenuRequested += (_, e) => e.Handled = !e.ContextMenuTarget.IsEditable;
#endif
        core.NewWindowRequested += (_, e) => e.Handled = true;

        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ServeEmbeddedFile;
        core.WebMessageReceived += OnWebMessage;
        core.NavigationCompleted += (_, _) => _webReady = true;
        // The page reads the theme preference before its first paint.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.ttTheme = {JsonSerializer.Serialize(_app.Settings.Theme)};");
        core.Navigate(Origin + "index.html");
    }

    private void ServeEmbeddedFile(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path.Length == 0) path = "index.html";

        var env = Web.CoreWebView2.Environment;
        var stream = EmbeddedContent.Open(path);
        e.Response = stream is null
            ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
            : env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {EmbeddedContent.MimeType(path)}");
    }

    // ───────────── page ↔ host messages ─────────────
    // Page → host: { id, method, args }. Host → page: { type: "reply", id, ok, data | error }
    // or { type: "event", name, data } — see the host object in wwwroot/index.html.

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (JsonNode.Parse(e.WebMessageAsJson) is not JsonObject msg) return;
        int id = (int?)msg["id"] ?? 0;
        string method = (string?)msg["method"] ?? "";
        var args = msg["args"] as JsonObject ?? [];
        try
        {
            object? data = await HandleAsync(method, args);
            Post(new { type = "reply", id, ok = true, data });
        }
        catch (Exception ex)
        {
            Log.Write($"запит {method}: {ex}");
            Post(new { type = "reply", id, ok = false, error = ex.Message });
        }
    }

    private async Task<object?> HandleAsync(string method, JsonObject a)
    {
        var settings = _app.Settings;
        var exclude = settings.ExcludeSet;
        switch (method)
        {
            case "overview":
            {
                var (from, to) = Range(a);
                return await Task.Run(() => Stats.GetOverview(from, to, exclude));
            }
            case "total":
            {
                var (from, to) = Range(a);
                return await Task.Run(() => Stats.GetTotal(from, to, exclude));
            }
            case "firstDay":
                return (await Task.Run(Stats.GetFirstDay))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "status":
                return _app.Status;
            case "setPaused":
                _app.SetPaused((bool?)a["value"] ?? false);
                return null;
            case "settings":
                return await SettingsViewAsync();
            case "programs":
                return await Task.Run(Stats.GetPrograms);
            case "updateSettings":
                if (a["idleMinutes"] is JsonNode idle) settings.IdleMinutes = Math.Clamp((int)idle, 1, 120);
                if (a["theme"] is JsonNode theme)
                {
                    settings.Theme = (string?)theme is "light" or "dark" ? (string)theme! : "system";
                    ApplyTheme(IsDark(settings.Theme));
                }
                if (a["autostart"] is JsonNode autostart) Autostart.Set((bool)autostart);
                if (a["autoUpdate"] is JsonNode autoUpdate) settings.AutoUpdate = (bool)autoUpdate;
                settings.Save();
                return await SettingsViewAsync();
            case "exclude":
                settings.SetExclude(settings.Exclude.Append((string?)a["exe"] ?? ""));
                settings.Save();
                return await SettingsViewAsync();
            case "include":
            {
                string exe = (string?)a["exe"] ?? "";
                settings.SetExclude(settings.Exclude.Where(x => !string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)));
                settings.Save();
                return await SettingsViewAsync();
            }
            case "export":
                return await ExportAsync(a);
            case "openDataFolder":
                Paths.EnsureDataDir();
                Process.Start(new ProcessStartInfo(Paths.DataDir) { UseShellExecute = true });
                return null;
            case "revealFile":
                Process.Start("explorer.exe", $"/select,\"{(string?)a["path"]}\"");
                return null;
            case "systemTheme":
                ApplyTheme((bool?)a["dark"] ?? false);
                return null;
            case "updateState":
                return _app.Update;
            case "checkUpdates":
                return await _app.CheckUpdatesAsync();
            case "installUpdate":
                return await _app.InstallUpdateAsync();
            case "restartToUpdate":
                _app.RestartToUpdate(inTray: false);
                return null;
            case "openRepo":
                Process.Start(new ProcessStartInfo(AppInfo.RepoUrl + "/releases") { UseShellExecute = true });
                return null;
            default:
                throw new InvalidOperationException($"Невідомий запит: {method}");
        }
    }

    private async Task<object> SettingsViewAsync()
    {
        var settings = _app.Settings;
        var names = (await Task.Run(Stats.GetPrograms)).ToDictionary(p => p.Exe, p => p.Name, StringComparer.OrdinalIgnoreCase);
        return new
        {
            exclude = settings.Exclude.Select(exe => new
            {
                exe,
                name = names.GetValueOrDefault(exe) ?? Core.Foreground.Aliases.GetValueOrDefault(exe) ?? exe,
            }),
            idleMinutes = settings.IdleMinutes,
            autostart = Autostart.IsEnabled,
            theme = settings.Theme,
            autoUpdate = settings.AutoUpdate,
            version = AppInfo.Version,
        };
    }

    private async Task<string?> ExportAsync(JsonObject a)
    {
        var (from, to) = Range(a);
        string period = from == to
            ? from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : $"{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var dialog = new SaveFileDialog
        {
            Title = "Експорт у CSV",
            FileName = $"TimeTracker_{period}.csv",
            Filter = "CSV для Excel (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return null;
        var exclude = _app.Settings.ExcludeSet;
        await Task.Run(() => Stats.ExportCsv(from, to, exclude, dialog.FileName));
        return dialog.FileName;
    }

    private static (DateOnly From, DateOnly To) Range(JsonObject a) =>
        (ParseDate((string?)a["from"]), ParseDate((string?)a["to"]));

    private static DateOnly ParseDate(string? value) =>
        DateOnly.ParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal void PushEvent(string name, object? data)
    {
        if (_webReady && IsVisible) Post(new { type = "event", name, data });
    }

    private void Post(object message) => Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));

    // ───────────── theme ─────────────

    private static bool IsDark(string theme) => theme switch
    {
        "dark" => true,
        "light" => false,
        _ => SystemUsesDarkTheme(),
    };

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Title bar and the area behind the page follow the page theme (no white flash on dark).</summary>
    private void ApplyTheme(bool dark)
    {
        var bg = dark ? Color.FromRgb(0x0F, 0x12, 0x14) : Color.FromRgb(0xF6, 0xF7, 0xF8);
        Background = new SolidColorBrush(bg);
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Win32.SetDarkTitleBar(hwnd, dark);
    }
}
