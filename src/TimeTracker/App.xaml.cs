using System.Windows;
using System.Windows.Threading;
using TimeTracker.Core;
using TimeTracker.Host;

namespace TimeTracker;

/// <summary>Live state shown in the window header and the tray tooltip.</summary>
internal sealed record Status(bool Paused, string? Current, double TodayTotal);

public partial class App : Application
{
    // One copy at a time: two trackers would record the same minutes twice.
    private Mutex? _singleInstance;
    // A second launch signals this event, and the running copy shows its window.
    private EventWaitHandle? _showRequest;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private DispatcherTimer? _statusTimer;
    private volatile bool _quitting;

    private DispatcherTimer? _updateTimer;
    private bool _updateBusy;

    internal Settings Settings { get; private set; } = null!;
    internal Tracker Tracker { get; private set; } = null!;
    internal Status Status { get; private set; } = new(false, null, 0);
    internal UpdateState Update { get; private set; } = Updater.Initial;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Installer mode: this is the freshly downloaded exe started from %TEMP% (see Updater.ApplyOnExit).
        if (e.Args.Contains("--apply-update"))
        {
            Shutdown(Updater.ApplyStaged(e.Args));
            return;
        }

        _singleInstance = new Mutex(true, @"Local\TimeTracker.SingleInstance", out bool isFirst);
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TimeTracker.Show");
        if (!isFirst)
        {
            _showRequest.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("помилка інтерфейсу: " + args.Exception);
            args.Handled = true;
        };

        Settings = Settings.Load();
        Tracker = new Tracker(Settings);
        Tracker.Start();

        _window = new MainWindow(this);
        _tray = new TrayIcon(ShowMainWindow, () => SetPaused(!Tracker.Paused), () => RestartToUpdate(inTray: !_window.IsVisible), Quit);
        // Windows is shutting down / logging off: install a downloaded update on the way out.
        SessionEnding += (_, _) => Updater.ApplyOnExit(restart: false, restartInTray: false);

        new Thread(WaitForShowRequests) { IsBackground = true, Name = "show-request" }.Start();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        _ = RefreshStatusAsync();

        // Updates: tidy up after the previous install, then check every few hours (it's a tray app that runs for days).
        Task.Delay(2000).ContinueWith(_ => Updater.CleanupStale());
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _updateTimer.Tick += async (_, _) => await AutoUpdateAsync();
        _updateTimer.Start();
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(5000);
            await AutoUpdateAsync();
        });

        // Autostart passes --tray: start quietly, counting in the background.
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            ShowMainWindow();
    }

    private void WaitForShowRequests()
    {
        while (_showRequest!.WaitOne())
        {
            if (_quitting) return;
            Dispatcher.BeginInvoke(ShowMainWindow);
        }
    }

    internal void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Called when the user closes the window (it only hides).</summary>
    internal void OnWindowHidden()
    {
        if (Settings.TrayHintShown) return;
        Settings.TrayHintShown = true;
        Settings.Save();
        _tray?.ShowHint("TimeTracker працює у фоні",
            "Облік продовжується. Клікніть по іконці годинника, щоб відкрити вікно.");
    }

    internal void SetPaused(bool paused)
    {
        Tracker.Paused = paused;
        _tray?.SetPaused(paused);
        _ = RefreshStatusAsync();
    }

    internal async Task RefreshStatusAsync()
    {
        var exclude = Settings.ExcludeSet;
        var today = DateOnly.FromDateTime(DateTime.Now);
        double total;
        try
        {
            total = await Task.Run(() => Stats.GetTotal(today, today, exclude));
        }
        catch (Exception ex)
        {
            Log.Write("не вдалося порахувати підсумок: " + ex.Message);
            return;
        }

        Status = new Status(Tracker.Paused, Tracker.Paused ? null : Tracker.Current, total);
        _tray?.SetTooltip($"TimeTracker{(Status.Paused ? " · пауза" : "")}\nСьогодні: {Format.Duration(total)}");
        _window?.PushEvent("status", Status);
    }

    // ───────────── updates ─────────────

    private async Task AutoUpdateAsync()
    {
        if (!Settings.AutoUpdate || Updater.Source != "release" || Updater.IsStaged) return;
        if (Unix.Now - Settings.LastUpdateCheck < Updater.CheckInterval.TotalSeconds) return;
        var state = await CheckUpdatesAsync();
        if (state.Available && state.Error is null) await InstallUpdateAsync();
    }

    internal async Task<UpdateState> CheckUpdatesAsync()
    {
        if (_updateBusy) return Update;
        _updateBusy = true;
        try
        {
            Update = await Updater.CheckAsync();
        }
        catch (Exception ex)
        {
            Log.Write("перевірка оновлень: " + ex.Message);
            Update = Update with { Error = ex.Message, Available = false, Checked = true };
        }
        finally
        {
            _updateBusy = false;
        }
        Settings.LastUpdateCheck = Unix.Now;
        Settings.Save();
        _window?.PushEvent("update", Update);
        return Update;
    }

    internal async Task<UpdateState> InstallUpdateAsync()
    {
        if (_updateBusy) return Update;
        _updateBusy = true;
        try
        {
            Update = await Updater.DownloadAsync();
            _tray?.SetUpdateReady(Update.Latest);
            _tray?.ShowHint($"Оновлення {Update.Latest} готове",
                "Встановиться під час наступного запуску. Щоб оновити зараз — меню іконки в треї.");
        }
        catch (Exception ex)
        {
            Log.Write("завантаження оновлення: " + ex.Message);
            Update = Update with { Error = ex.Message };
        }
        finally
        {
            _updateBusy = false;
        }
        _window?.PushEvent("update", Update);
        return Update;
    }

    /// <summary>Quit and let the downloaded version install itself and start again.</summary>
    internal void RestartToUpdate(bool inTray)
    {
        if (!Updater.IsStaged) return;
        Updater.ApplyOnExit(restart: true, restartInTray: inTray);
        Quit();
    }

    internal void Quit()
    {
        _quitting = true;
        _showRequest?.Set(); // release the waiting thread
        _statusTimer?.Stop();
        _updateTimer?.Stop();
        // A downloaded update installs after the app has closed.
        Updater.ApplyOnExit(restart: false, restartInTray: false);
        Tracker.Stop();
        _tray?.Dispose();
        _window?.CloseForReal();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop();
        _singleInstance?.Dispose();
        _showRequest?.Dispose();
        base.OnExit(e);
    }
}
