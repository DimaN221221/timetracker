using System.IO;

namespace TimeTracker.Core;

/// <summary>Where the app keeps its data. The same folder the Python version used, so history carries over.</summary>
internal static class Paths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");

    public static readonly string Database = Path.Combine(DataDir, "activity.db");
    public static readonly string Settings = Path.Combine(DataDir, "settings.json");
    public static readonly string Log = Path.Combine(DataDir, "tracker.log");
    public static readonly string WebViewData = Path.Combine(DataDir, "WebView2");

    public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Paths.EnsureDataDir();
                File.AppendAllText(Paths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }
}
