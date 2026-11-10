using System.Reflection;

namespace DigitalLogin;

/// <summary>
/// The running build's version, read from the assembly rather than a hand-maintained string.
/// <para>
/// The value comes from <see cref="AssemblyInformationalVersionAttribute"/>. The release workflow stamps the
/// tag into it, and SourceLink suffixes the commit, giving e.g. <c>0.2.0+fe12a13c9ce22ebb…</c>.
/// </para>
/// </summary>
public static class AppVersion
{
    private const string Unknown = "unknown";
    private const int ShortCommitLength = 8;

    static AppVersion()
    {
        // The entry assembly is the application being run, which is the one carrying the real version.
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
        Full = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? Unknown;

        var plus = Full.IndexOf('+');
        Display = plus >= 0 ? Full[..plus] : Full;
        Commit = plus >= 0 ? Full[(plus + 1)..] : null;
        ShortCommit = Commit is not null ? Commit[..Math.Min(ShortCommitLength, Commit.Length)] : null;
    }

    /// <summary>Everything the assembly reports, e.g. <c>0.2.0+fe12a13c9ce22ebb…</c>.</summary>
    public static string Full { get; }

    /// <summary>Just the version, e.g. <c>0.2.0</c>, or <c>1.0.0-rc1</c> for a prerelease.</summary>
    public static string Display { get; }

    /// <summary>The commit the build came from, when recorded.</summary>
    public static string? Commit { get; }

    /// <summary>The leading characters of <see cref="Commit"/>.</summary>
    public static string? ShortCommit { get; }
}
