namespace DigitalLogin.Updates;

/// <summary>
/// Checks applied to a download before the app runs it.
/// <para>
/// This is the one place where a file arrives from the internet and is then executed, so the URL and the
/// file name are treated as untrusted input even though they came from the GitHub API over TLS.
/// </para>
/// </summary>
public static class UpdateDownload
{
    /// <summary>Where GitHub serves releases and their assets from.</summary>
    private static readonly string[] AllowedHostSuffixes =
    {
        "github.com",
        "githubusercontent.com",
    };

    /// <summary>
    /// True for an <c>https</c> URL on a GitHub host. Anything else is refused: a spoofed API response must
    /// not be able to point the installer step at an arbitrary executable.
    /// </summary>
    public static bool IsTrustedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host;
        return AllowedHostSuffixes.Any(suffix =>
            // The suffix must be a whole label, so "evil-github.com" does not pass as "github.com".
            host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reduces an asset name to something safe to write into a temporary directory: no directory parts, no
    /// invalid characters, never empty. The asset name is chosen by whoever published the release.
    /// </summary>
    public static string SafeFileName(string? assetName, string fallback = "DigitalLogin-Setup.exe")
    {
        // GetFileName strips anything path-like, including "..\" traversal.
        var name = Path.GetFileName(assetName?.Trim() ?? "");
        if (name.Length > 0)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            // "." and ".." survive the filter above but are not file names.
            if (cleaned.Trim('.', ' ').Length > 0) return cleaned;
        }
        return fallback;
    }
}
