using System.Reflection;

namespace TimeTracker.Core;

/// <summary>Name, version and repository of the app (used by updates, the window and the release build).</summary>
internal static class AppInfo
{
    public const string Name = "TimeTracker";

    /// <summary>GitHub repository that releases come from: "owner/name".</summary>
    public const string Repo = "DimaN221221/timetracker";
    public const string RepoUrl = "https://github.com/" + Repo;

    /// <summary>Windows build attached to every GitHub release (plus "&lt;asset&gt;.sha256").</summary>
    public const string ReleaseAsset = Name + "-windows-x64.zip";

    /// <summary>"2.1.0" — from &lt;Version&gt; in TimeTracker.csproj.</summary>
    public static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // .NET appends "+<commit>" to the informational version; the release tag has only the numbers.
        return info?.Split('+')[0] ?? "0.0.0";
    }
}
