using System.Reflection;

namespace LunaDns.App;

/// <summary>Phiên bản (từ &lt;Version&gt; của csproj) và repo GitHub (&lt;LunaDnsRepository&gt;, KH7 T0).</summary>
internal static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static Version Version { get; } = Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string Repository { get; } =
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "GitHubRepository").Value
        ?? throw new InvalidOperationException("Thiếu <LunaDnsRepository> trong csproj.");
}
