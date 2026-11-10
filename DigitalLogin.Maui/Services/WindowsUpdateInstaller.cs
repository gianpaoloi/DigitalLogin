using System.Diagnostics;
using System.Security.Cryptography;
using DigitalLogin.Updates;
using Microsoft.Maui.ApplicationModel;

namespace DigitalLogin.Maui.Services;

/// <summary>
/// Updates an installed copy of the desktop app by downloading the release's Inno Setup package and
/// handing over to it.
/// <para>
/// There is no in-place file replacement: the app runs from the directory that has to be overwritten, and
/// the setup package already knows how to do that, keeps the Start-menu entries and adds the WebView2
/// runtime. Re-running it silently is a supported upgrade path; the install is per-user, so no elevation.
/// </para>
/// <para>
/// The setup package needs the <c>/UPDATED=1</c> switch for this: a silent install skips the "launch the
/// app" step, so without it the app would download an update, close, and never come back.
/// See <c>DigitalLogin.Maui/installer/setup.iss</c>.
/// </para>
/// </summary>
public sealed class WindowsUpdateInstaller : IUpdateInstaller
{
    /// <summary>
    /// <c>/SILENT</c> shows a progress window, not <c>/VERYSILENT</c>: a self-update that gives no sign of
    /// running looks like a crash. <c>/NORESTARTAPPLICATIONS</c> leaves restarting to the <c>/UPDATED=1</c>
    /// entry, so the app cannot be started twice.
    /// </summary>
    private const string InstallerArguments =
        "/SILENT /NOCANCEL /NORESTART /NORESTARTAPPLICATIONS /UPDATED=1";

    private const int DownloadBufferSize = 128 * 1024;

    /// <summary>Long: an ~80 MB package, possibly over a corporate link, with a progress bar and a cancel button.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>How long to give the installer to fail before assuming it is running.</summary>
    private static readonly TimeSpan FailFastWindow = TimeSpan.FromSeconds(3);

    private static string DownloadDirectory =>
        Path.Combine(Path.GetTempPath(), "DigitalLogin-update");

    public WindowsUpdateInstaller()
    {
        // Evaluated once: the answer cannot change while the app runs.
        UnavailableReason = InstallLocation.IsSetupInstall(AppContext.BaseDirectory)
            ? null
            : "This copy was not installed by the DigitalLogin setup. It is a portable or development build, " +
              "and installing over it would leave this copy behind on the old version. Download the new " +
              "release from GitHub instead.";
    }

    public bool CanInstall => UnavailableReason is null;

    public string? UnavailableReason { get; }

    public async Task InstallAsync(UpdateInfo update, IProgress<UpdateDownloadProgress>? progress,
                                   CancellationToken cancellationToken = default)
    {
        if (!CanInstall) throw new InvalidOperationException(UnavailableReason);

        if (!update.HasInstaller)
            throw new InvalidOperationException(
                $"Release {update.TagName} has no installer attached, so it cannot be installed automatically.");

        // The URL came from a JSON document off the network and decides what gets executed next, so it is
        // checked rather than trusted.
        if (!UpdateDownload.IsTrustedUrl(update.InstallerUrl))
            throw new InvalidOperationException(
                $"The installer for {update.TagName} is not hosted on GitHub ({update.InstallerUrl}); " +
                "it will not be downloaded.");

        var file = await DownloadAsync(update, progress, cancellationToken).ConfigureAwait(false);
        await VerifyAsync(file, update.InstallerSha256, cancellationToken).ConfigureAwait(false);
        await StartAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> DownloadAsync(UpdateInfo update,
                                                    IProgress<UpdateDownloadProgress>? progress,
                                                    CancellationToken cancellationToken)
    {
        var directory = DownloadDirectory;
        TryClear(directory);
        Directory.CreateDirectory(directory);

        var file = Path.Combine(directory, UpdateDownload.SafeFileName(update.InstallerName));

        using var http = new HttpClient { Timeout = DownloadTimeout };
        using var response = await http
            .GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Fall back to the size the API reported, so the progress bar still has a total.
        var total = response.Content.Headers.ContentLength ?? update.InstallerSize;

        try
        {
            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(file, FileMode.Create, FileAccess.Write,
                                                         FileShare.None, DownloadBufferSize, useAsync: true);

            var buffer = new byte[DownloadBufferSize];
            long received = 0;
            int read;
            progress?.Report(new UpdateDownloadProgress(0, total));
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new UpdateDownloadProgress(received, total));
            }
        }
        catch
        {
            // A half-written installer must not be left where a later attempt might run it.
            TryDelete(file);
            throw;
        }

        return file;
    }

    /// <summary>
    /// Checks the download against the hash published with the release. Skipped only when none was
    /// published; the transport was still HTTPS to GitHub in that case.
    /// </summary>
    private static async Task VerifyAsync(string file, string? expected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;

        string actual;
        await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                 DownloadBufferSize, useAsync: true))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexString(hash);
        }

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return;

        TryDelete(file);
        throw new InvalidDataException(
            "The downloaded installer does not match the SHA256 published with the release, so it has been " +
            $"deleted rather than run.{Environment.NewLine}Expected {expected}{Environment.NewLine}Received {actual}");
    }

    /// <summary>
    /// Starts the installer and closes the app, because the installer has to replace this executable.
    /// Setup restarts the new build afterwards, via <c>/UPDATED=1</c>.
    /// </summary>
    private static async Task StartAsync(string file, CancellationToken cancellationToken)
    {
        using var setup = Process.Start(new ProcessStartInfo(file)
        {
            Arguments = InstallerArguments,
            // Required for a .exe launched by the shell; also what surfaces a SmartScreen prompt.
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException($"The installer at {file} could not be started.");

        // Quitting is not undoable, so give an installer that refuses to run a moment to say so; otherwise
        // the app would vanish and come back on the old version with nothing to explain why.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FailFastWindow);
        try
        {
            await setup.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            // It exited already, so it never got as far as installing anything.
            throw new InvalidOperationException(
                $"The installer exited immediately with code {setup.ExitCode} without installing the update.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Still running after the window: the normal case.
        }

        // Quit rather than Environment.Exit so MAUI closes the window and the WebView cleanly. Setup closes
        // applications still holding files in the install directory, so a slow exit delays the update rather
        // than breaking it.
        MainThread.BeginInvokeOnMainThread(() => Microsoft.Maui.Controls.Application.Current?.Quit());
    }

    private static void TryClear(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left over from an earlier attempt and still locked; the download overwrites what it needs.
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do; the caller is already reporting a failure.
        }
    }
}
