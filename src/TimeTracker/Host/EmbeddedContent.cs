using System.IO;
using System.Reflection;

namespace TimeTracker.Host;

/// <summary>Files from the wwwroot folder, compiled into the exe (see TimeTracker.csproj).</summary>
internal static class EmbeddedContent
{
    private static readonly Assembly Self = typeof(EmbeddedContent).Assembly;

    // RecursiveDir in resource names uses backslashes; normalise to URL-style paths.
    private static readonly Dictionary<string, string> Resources = Self.GetManifestResourceNames()
        .Where(n => n.StartsWith("wwwroot", StringComparison.Ordinal))
        .ToDictionary(n => n["wwwroot/".Length..].Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase);

    public static Stream? Open(string path) =>
        Resources.TryGetValue(path, out var name) ? Self.GetManifestResourceStream(name) : null;

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };
}
