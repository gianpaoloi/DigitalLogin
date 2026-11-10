namespace DigitalLogin.Updates;

/// <summary>
/// Tells apart the ways this app can sit on disk, because only one of them can be updated in place.
/// </summary>
public static class InstallLocation
{
    /// <summary>Broad enough for the filesystem to do the cheap part; the exact shape is checked below.</summary>
    private const string UninstallerPattern = "unins*.exe";

    /// <summary>Length of <c>unins000.exe</c>, the name Inno Setup writes.</summary>
    private const int UninstallerNameLength = 12;

    /// <summary>
    /// True when the directory is an installation made by the project's setup package, the only case where
    /// running that setup again is an update rather than a second copy of the app. The portable ZIP has no
    /// uninstaller and must not be updated over, so it is steered to the download page instead.
    /// </summary>
    public static bool IsSetupInstall(string? applicationDirectory)
    {
        if (string.IsNullOrWhiteSpace(applicationDirectory)) return false;

        try
        {
            return Directory.Exists(applicationDirectory)
                && Directory.EnumerateFiles(applicationDirectory, UninstallerPattern)
                            .Any(path => IsInnoUninstaller(Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable: assume it is not updatable rather than offer an update that would then fail.
            return false;
        }
    }

    /// <summary><c>unins</c>, exactly three digits, then <c>.exe</c>. Inno Setup starts at unins000.exe.</summary>
    private static bool IsInnoUninstaller(string fileName) =>
        fileName.Length == UninstallerNameLength
        && fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
        && char.IsAsciiDigit(fileName[5])
        && char.IsAsciiDigit(fileName[6])
        && char.IsAsciiDigit(fileName[7])
        && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}
