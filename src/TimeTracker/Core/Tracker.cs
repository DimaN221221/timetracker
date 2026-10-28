using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TimeTracker.Core;

/// <summary>What the user is looking at right now: process file, readable program name, window title.</summary>
internal readonly record struct Sample(string Exe, string App, string Title);

/// <summary>
/// Background thread: every few seconds records the foreground window into activity.db.
/// Time without keyboard/mouse input longer than the idle threshold is not counted.
/// </summary>
internal sealed class Tracker
{
    private const int IntervalMs = 5000;

    private readonly Settings _settings;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private volatile bool _paused;
    private volatile string? _current;

    public Tracker(Settings settings)
    {
        _settings = settings;
        _thread = new Thread(Run) { IsBackground = true, Name = "tracker" };
    }

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>Program being counted at the moment, or null (idle, paused, excluded).</summary>
    public string? Current => _current;

    public void Start() => _thread.Start();

    public void Stop()
    {
        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        using var db = Database.Open();
        Log.Write("облік запущено");
        double? lastTick = null;
        long? lastId = null;
        Sample? lastKey = null;

        while (true)
        {
            double now = Unix.Now;
            double idleLimit = _settings.IdleMinutes * 60;
            double idle = Win32.IdleSeconds();
            Sample? cur = null;
            if (!_paused && idle < idleLimit)
            {
                try
                {
                    cur = Foreground.Sample(_settings.ExcludeSet);
                }
                catch (Exception ex)
                {
                    Log.Write("помилка читання вікна: " + ex);
                }
            }

            // After sleep/hibernation the gap between ticks is large: don't stretch the last span over it.
            bool contiguous = lastTick is double lt && now - lt <= IntervalMs / 1000.0 * 3;
            try
            {
                if (lastId is long id && contiguous)
                {
                    double end = idle >= idleLimit ? now - idle : now; // cut off the idle tail
                    Database.Execute(db, """UPDATE spans SET "end" = MAX(start, $end) WHERE id = $id""",
                        ("$end", end), ("$id", id));
                }

                if (cur is not Sample s)
                {
                    lastId = null;
                    lastKey = null;
                }
                else if (!contiguous || s != lastKey)
                {
                    using var cmd = Database.Command(db, """
                        INSERT INTO spans(start, "end", exe, app, title) VALUES ($t, $t, $exe, $app, $title);
                        SELECT last_insert_rowid();
                        """, ("$t", now), ("$exe", s.Exe), ("$app", s.App), ("$title", s.Title));
                    lastId = (long)cmd.ExecuteScalar()!;
                    lastKey = s;
                }
            }
            catch (SqliteException ex)
            {
                Log.Write("помилка бази: " + ex.Message);
            }

            _current = cur?.App;
            lastTick = now;
            if (_stop.Wait(IntervalMs)) break;
        }
        Log.Write("облік зупинено");
    }
}

internal static class Foreground
{
    /// <summary>System windows that always count as idle (lock screen etc.).</summary>
    private static readonly HashSet<string> AlwaysIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "lockapp.exe", "logonui.exe", "screensaver.exe", "timetracker.exe",
    };

    /// <summary>Readable names for well-known programs; others use the exe's file description.</summary>
    public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer.exe"] = "Провідник",
        ["fl64.exe"] = "FL Studio",
        ["fl.exe"] = "FL Studio",
        ["blender.exe"] = "Blender",
        ["unity.exe"] = "Unity",
        ["unity hub.exe"] = "Unity Hub",
        ["code.exe"] = "VS Code",
        ["devenv.exe"] = "Visual Studio",
        ["windowsterminal.exe"] = "Windows Terminal",
        ["powershell.exe"] = "PowerShell",
        ["cmd.exe"] = "Командний рядок",
        ["telegram.exe"] = "Telegram",
        ["steamwebhelper.exe"] = "Steam",
        ["steam.exe"] = "Steam",
    };

    // Direction marks some apps (Telegram) put into titles.
    private static readonly char[] Invisible =
        ['‎', '‏', '‪', '‫', '‬', '‭', '‮', '⁦', '⁧', '⁨', '⁩'];

    private static readonly ConcurrentDictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    public static Sample? Sample(IReadOnlySet<string> exclude)
    {
        IntPtr hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        uint pid = Win32.WindowProcessId(hwnd);
        if (pid == OwnPid) return null;
        string? path = Win32.ProcessPath(pid);
        if (path is null) return null;

        string exe = Path.GetFileName(path).ToLowerInvariant();
        if (AlwaysIgnored.Contains(exe) || exclude.Contains(exe)) return null;

        string title = string.Concat(Win32.WindowTitle(hwnd).Where(c => Array.IndexOf(Invisible, c) < 0)).Trim();
        if (title.Length > 300) title = title[..300];
        return new Sample(exe, AppName(path, exe, title), title);
    }

    private static string AppName(string path, string exe, string title)
    {
        if (exe == "applicationframehost.exe") // UWP apps (Calculator, Settings...) all run in this host
            return title.Length > 0 ? title : "UWP-застосунок";
        if (Aliases.TryGetValue(exe, out var alias)) return alias;
        return Descriptions.GetOrAdd(path, p =>
        {
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(p).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(desc)) return desc;
            }
            catch (FileNotFoundException)
            {
            }
            return Path.GetFileNameWithoutExtension(p);
        });
    }
}
