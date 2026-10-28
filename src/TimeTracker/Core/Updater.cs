using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TimeTracker.Core;

/// <summary>What the page shows about updates (sent as JSON).</summary>
internal sealed record UpdateState(
    string Source,          // "release" — the published exe; "none" — Debug build from Visual Studio
    string Current,
    string? Latest = null,
    bool Available = false, // a newer release exists
    bool Ready = false,     // downloaded and verified, installs on restart
    string? Error = null,
    bool Checked = false);

/// <summary>
/// Updates from GitHub Releases, the same way as in Remold:
/// the newest release's <see cref="AppInfo.ReleaseAsset"/> is downloaded, checked against its .sha256
/// and unpacked to %TEMP%. On restart (or when the app quits) the new TimeTracker.exe is started from
/// there with --apply-update: it waits for the old process to exit, copies itself over the installed
/// exe and, if asked, starts the updated app.
/// </summary>
internal static class Updater
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private const string ApiLatest = "https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest";
    private static readonly string StageRoot = Path.Combine(Path.GetTempPath(), "timetracker-update");
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "timetracker-update.log");
    private static readonly HttpClient Http = CreateClient();

    private static string? _tag, _zipUrl, _shaUrl;
    private static string? _stagedDir;

    public static string Source =>
#if DEBUG
        "none";
#else
        "release";
#endif

    public static bool IsStaged => _stagedDir is not null;

    public static UpdateState Initial => new(Source, AppInfo.Version);

    public static async Task<UpdateState> CheckAsync()
    {
        if (Source != "release") return Initial with { Checked = true };
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(ApiLatest));
        var root = doc.RootElement;
        _tag = root.GetProperty("tag_name").GetString() ?? "";
        _zipUrl = _shaUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == AppInfo.ReleaseAsset) _zipUrl = url;
            else if (name == AppInfo.ReleaseAsset + ".sha256") _shaUrl = url;
        }
        string latest = _tag.TrimStart('v', 'V');
        bool newer = ParseVersion(latest) > ParseVersion(AppInfo.Version);
        return Initial with { Latest = latest, Available = newer && !IsStaged, Ready = IsStaged, Checked = true };
    }

    /// <summary>Download, verify and unpack the latest release. Afterwards <see cref="IsStaged"/> is true.</summary>
    public static async Task<UpdateState> DownloadAsync()
    {
        if (Source != "release") throw new InvalidOperationException("Оновлення працюють лише в зібраній програмі.");
        if (_tag is null) await CheckAsync();
        if (_zipUrl is null) throw new InvalidOperationException($"У релізі немає файлу {AppInfo.ReleaseAsset}.");

        if (Directory.Exists(StageRoot)) Directory.Delete(StageRoot, recursive: true);
        Directory.CreateDirectory(StageRoot);
        string archive = Path.Combine(StageRoot, AppInfo.ReleaseAsset);

        await using (var input = await Http.GetStreamAsync(_zipUrl))
        await using (var output = File.Create(archive))
            await input.CopyToAsync(output);

        if (_shaUrl is not null)
        {
            string expected = (await Http.GetStringAsync(_shaUrl)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            string actual;
            await using (var file = File.OpenRead(archive))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(StageRoot, recursive: true);
                throw new InvalidOperationException("Контрольна сума архіву не збігається (SHA-256).");
            }
        }

        string target = Path.Combine(StageRoot, "new");
        using (var zip = ZipFile.OpenRead(archive))
        {
            // Guard against entries like ../../file.
            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                string dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Небезпечний шлях в архіві: {entry.FullName}");
            }
            zip.ExtractToDirectory(target);
        }

        string? exe = Directory.EnumerateFiles(target, AppInfo.Name + ".exe", SearchOption.AllDirectories).FirstOrDefault();
        _stagedDir = Path.GetDirectoryName(exe) ?? throw new InvalidOperationException($"В архіві немає {AppInfo.Name}.exe.");
        return Initial with { Latest = _tag!.TrimStart('v', 'V'), Ready = true, Checked = true };
    }

    /// <summary>Start the staged version, which installs itself once this process has exited.</summary>
    public static void ApplyOnExit(bool restart, bool restartInTray)
    {
        if (_stagedDir is null) return;
        string appDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        var psi = new ProcessStartInfo(Path.Combine(_stagedDir, AppInfo.Name + ".exe"))
        {
            UseShellExecute = false,
            WorkingDirectory = StageRoot,
        };
        foreach (var arg in new[] { "--apply-update", _stagedDir, "--target", appDir, "--wait-pid", Environment.ProcessId.ToString() })
            psi.ArgumentList.Add(arg);
        if (restart) psi.ArgumentList.Add(restartInTray ? "--restart-tray" : "--restart");
        Process.Start(psi);
        _stagedDir = null; // started once; don't start a second installer on quit
    }

    /// <summary>
    /// --apply-update mode: wait for the old app to exit, copy the new files over its folder
    /// and optionally start the updated app. Returns the process exit code.
    /// </summary>
    public static int ApplyStaged(string[] args)
    {
        string src = Arg(args, "--apply-update")!, dst = Arg(args, "--target")!;
        if (int.TryParse(Arg(args, "--wait-pid"), out int pid))
        {
            IntPtr handle = OpenProcess(SYNCHRONIZE, false, pid);
            if (handle != IntPtr.Zero)
            {
                WaitForSingleObject(handle, 120_000);
                CloseHandle(handle);
            }
        }

        var failed = new List<string>();
        foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dst, Path.GetRelativePath(src, file));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The old exe can stay locked for a moment (antivirus, Explorer).
                    if (attempt == 39)
                    {
                        failed.Add($"{target}: {ex.Message}");
                        break;
                    }
                    Thread.Sleep(250);
                }
            }
        }
        WriteLog($"applied {src} -> {dst}: {(failed.Count == 0 ? "ok" : $"{failed.Count} failed")}");
        failed.Take(20).ToList().ForEach(f => WriteLog("  " + f));

        bool restart = args.Contains("--restart"), inTray = args.Contains("--restart-tray");
        if (restart || inTray)
        {
            var psi = new ProcessStartInfo(Path.Combine(dst, AppInfo.Name + ".exe")) { UseShellExecute = false, WorkingDirectory = dst };
            if (inTray) psi.ArgumentList.Add("--tray");
            Process.Start(psi);
        }
        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>Remove the temp folder left by an update that has already been installed.</summary>
    public static void CleanupStale()
    {
        if (Source != "release" || IsStaged) return;
        try
        {
            if (Directory.Exists(StageRoot)) Directory.Delete(StageRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static Version ParseVersion(string text)
    {
        var parts = text.Trim().TrimStart('v', 'V').Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0)
            .Concat([0, 0, 0]).Take(3).ToArray();
        return new Version(parts[0], parts[1], parts[2]);
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void WriteLog(string text)
    {
        try
        {
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private const uint SYNCHRONIZE = 0x00100000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
