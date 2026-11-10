namespace DigitalLogin.Updates;

/// <summary>
/// Applies an update to the running installation.
/// <para>
/// An interface because only the MAUI desktop host can do it: it ships as a per-user Inno Setup package
/// that can be handed a new copy of itself. Hosts that cannot update register
/// <see cref="UnsupportedUpdateInstaller"/>, and the UI offers the download page instead.
/// </para>
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>True when <see cref="InstallAsync"/> can be expected to work.</summary>
    bool CanInstall { get; }

    /// <summary>Why <see cref="CanInstall"/> is false, phrased for the user. Null when it is true.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Downloads the release's installer, verifies it against the published hash, starts it and closes the
    /// app so it can be replaced. Returns only if something went wrong before the installer was started.
    /// </summary>
    Task InstallAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress,
                      CancellationToken cancellationToken = default);
}

/// <summary>Installer for a host that cannot update itself.</summary>
public sealed class UnsupportedUpdateInstaller : IUpdateInstaller
{
    public UnsupportedUpdateInstaller(string reason) => UnavailableReason = reason;

    public bool CanInstall => false;

    public string? UnavailableReason { get; }

    public Task InstallAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress,
                             CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(UnavailableReason);
}
