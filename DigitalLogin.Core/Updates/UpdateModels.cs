using System.Globalization;

namespace DigitalLogin.Updates;

/// <summary>Settings for the update check. Plain defaults: the app has no configuration file.</summary>
public sealed record UpdateOptions
{
    /// <summary>This project's own repository, the only place a release is published.</summary>
    public const string DefaultRepository = "gianpaoloi/DigitalLogin";

    /// <summary>
    /// Whether to contact GitHub at all. This is the app's only outbound network call apart from the
    /// login and registry calls the user starts, so it can be switched off.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary><c>owner/name</c> of the repository whose releases are checked.</summary>
    public string Repository { get; init; } = DefaultRepository;

    /// <summary>How long a completed check is reused before the network is touched again.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(6);

    /// <summary>How long to wait after a failed check before trying again.</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>How long one request gets before it is abandoned.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The page a user is sent to when the app cannot install the update itself.</summary>
    public string ReleasesPageUrl => $"https://github.com/{Repository}/releases";
}

/// <summary>A published release, reduced to the parts the app uses.</summary>
public sealed record UpdateInfo
{
    /// <summary>Parsed from the tag, so it can be ordered against the running build.</summary>
    public required ReleaseVersion Version { get; init; }

    /// <summary>The tag the release was cut from, e.g. <c>v1.2.3</c>.</summary>
    public required string TagName { get; init; }

    /// <summary>The release page, where a user is sent when the app cannot update itself.</summary>
    public required string ReleaseUrl { get; init; }

    public string? Title { get; init; }

    /// <summary>The release notes as the Markdown GitHub stores them. Rendered by the update dialog.</summary>
    public string? Notes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>True when GitHub marked the release as a prerelease.</summary>
    public bool IsPrerelease { get; init; }

    /// <summary>File name of the installer asset, e.g. <c>DigitalLogin-Setup-1.2.3.exe</c>.</summary>
    public string? InstallerName { get; init; }

    /// <summary>Direct download URL for the installer asset.</summary>
    public string? InstallerUrl { get; init; }

    /// <summary>Asset size in bytes, so the dialog can say how much it is about to download.</summary>
    public long? InstallerSize { get; init; }

    /// <summary>
    /// Expected SHA256 of the installer: the asset's own digest field, or failing that the hash line the
    /// release workflow writes into the notes. The download is checked against it before it is run.
    /// </summary>
    public string? InstallerSha256 { get; init; }

    /// <summary>False for a release that published no installer.</summary>
    public bool HasInstaller => !string.IsNullOrWhiteSpace(InstallerUrl);
}

/// <summary>Outcome of one update check.</summary>
public enum UpdateStatus
{
    /// <summary>Checking is switched off by configuration; no request was made.</summary>
    Disabled,

    /// <summary>The newest release is not newer than the running build.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    UpdateAvailable,

    /// <summary>The check could not be completed. <see cref="UpdateCheckResult.Message"/> says why.</summary>
    Failed,
}

/// <summary>
/// What a check found. A failure is a result, not an exception: the check runs in the background of
/// the app and must never surface as an error the user has to dismiss.
/// </summary>
public sealed record UpdateCheckResult(UpdateStatus Status, UpdateInfo? Release = null, string? Message = null)
{
    public static readonly UpdateCheckResult Disabled = new(UpdateStatus.Disabled);

    /// <summary>True only when there is a newer release and its details were read.</summary>
    public bool IsUpdateAvailable => Status == UpdateStatus.UpdateAvailable && Release is not null;
}

/// <summary>How far a download has got, for the progress bar in the update dialog.</summary>
public readonly record struct UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>0..1, or null when the total is unknown; the bar is then indeterminate.</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}
