using System.Net;
using DigitalLogin.Updates;
using Xunit;

namespace DigitalLogin.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2")]
    [InlineData("1.0.0.0", "1.0.0.0")]
    [InlineData("1.3.0-rc1", "1.3.0-rc1")]
    [InlineData("1.2.3+fe12a13c", "1.2.3")]
    public void Parses_tags_assembly_versions_and_build_metadata(string input, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(input, out var v));
        Assert.Equal(expected, v!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("1.2.3-")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.3.4.5")]
    public void Rejects_what_is_not_a_version(string input)
    {
        Assert.False(ReleaseVersion.TryParse(input, out _));
    }

    [Fact]
    public void Compares_numerically_not_as_text()
    {
        Assert.True(Parse("1.10.0").IsNewerThan(Parse("1.9.0")));
    }

    [Fact]
    public void A_release_outranks_its_prerelease()
    {
        Assert.True(Parse("1.3.0").IsNewerThan(Parse("1.3.0-rc1")));
        Assert.False(Parse("1.3.0-rc1").IsNewerThan(Parse("1.3.0")));
    }

    [Fact]
    public void Prerelease_identifiers_follow_semver()
    {
        Assert.True(Parse("1.0.0-rc.2").IsNewerThan(Parse("1.0.0-rc.1")));
        Assert.True(Parse("1.0.0-rc.1.2").IsNewerThan(Parse("1.0.0-rc.1")));
        // Numeric identifiers rank below alphanumeric ones.
        Assert.True(Parse("1.0.0-rc.beta").IsNewerThan(Parse("1.0.0-rc.2")));
    }

    [Fact]
    public void Missing_fields_are_zero_and_build_metadata_is_ignored()
    {
        Assert.Equal(Parse("1.2"), Parse("1.2.0"));
        Assert.Equal(Parse("1.2.3"), Parse("1.2.3+commit-a"));
    }

    private static ReleaseVersion Parse(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        return v!;
    }
}

public class GitHubReleaseParserTests
{
    private const string Digest = "8f0c1e2d3b4a5968778695a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4";

    [Fact]
    public void Reads_the_release_and_picks_the_setup_asset_over_the_zip()
    {
        var json = $$"""
        {
          "tag_name": "v0.2.0", "name": "DigitalLogin 0.2.0", "draft": false, "prerelease": false,
          "html_url": "https://github.com/gianpaoloi/DigitalLogin/releases/tag/v0.2.0",
          "body": "### Added\n- Something",
          "published_at": "2026-11-10T09:00:00Z",
          "assets": [
            { "name": "DigitalLogin-0.2.0-win-x64.zip", "browser_download_url": "https://github.com/a/b.zip", "size": 1, "digest": "sha256:{{Digest}}" },
            { "name": "DigitalLogin-Setup-0.2.0.exe", "browser_download_url": "https://github.com/a/setup.exe", "size": 82154201, "digest": "sha256:{{Digest}}" }
          ]
        }
        """;

        var update = GitHubReleaseParser.Parse(json);

        Assert.NotNull(update);
        Assert.Equal("0.2.0", update!.Version.ToString());
        Assert.Equal("DigitalLogin-Setup-0.2.0.exe", update.InstallerName);
        Assert.Equal("https://github.com/a/setup.exe", update.InstallerUrl);
        Assert.Equal(82154201, update.InstallerSize);
        Assert.Equal(Digest, update.InstallerSha256);
    }

    [Fact]
    public void Takes_the_hash_from_the_notes_when_there_is_no_digest()
    {
        var notes = "Installer:\r\n" + Digest.ToUpperInvariant() + "  DigitalLogin-Setup-0.2.0.exe\r\n";
        var json = $$"""
        {
          "tag_name": "v0.2.0", "html_url": "https://github.com/x", "body": {{System.Text.Json.JsonSerializer.Serialize(notes)}},
          "assets": [ { "name": "DigitalLogin-Setup-0.2.0.exe", "browser_download_url": "https://github.com/a/s.exe" } ]
        }
        """;

        var update = GitHubReleaseParser.Parse(json);

        Assert.Equal(Digest.ToUpperInvariant(), update!.InstallerSha256);
    }

    [Fact]
    public void A_release_with_only_a_zip_has_no_installer()
    {
        var json = """
        { "tag_name": "v0.2.0", "html_url": "https://github.com/x",
          "assets": [ { "name": "DigitalLogin-0.2.0-win-x64.zip", "browser_download_url": "https://github.com/a/b.zip" } ] }
        """;

        Assert.False(GitHubReleaseParser.Parse(json)!.HasInstaller);
    }

    [Fact]
    public void Drafts_and_unusable_payloads_are_not_releases()
    {
        Assert.Null(GitHubReleaseParser.Parse("""{ "tag_name": "v0.2.0", "draft": true }"""));
        Assert.Null(GitHubReleaseParser.Parse("""{ "tag_name": "nightly" }"""));
        Assert.Null(GitHubReleaseParser.Parse("not json"));
        Assert.Null(GitHubReleaseParser.Parse(""));
    }
}

public class UpdateDownloadTests
{
    [Theory]
    [InlineData("https://github.com/gianpaoloi/DigitalLogin/releases/download/v0.2.0/setup.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/setup.exe", true)]
    [InlineData("http://github.com/a/setup.exe", false)]
    [InlineData("https://evil-github.com/setup.exe", false)]
    [InlineData("https://github.com.evil.example/setup.exe", false)]
    [InlineData("file:///C:/temp/setup.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_https_github_hosts_are_trusted(string? url, bool expected)
    {
        Assert.Equal(expected, UpdateDownload.IsTrustedUrl(url));
    }

    [Theory]
    [InlineData("DigitalLogin-Setup-0.2.0.exe", "DigitalLogin-Setup-0.2.0.exe")]
    [InlineData("..\\..\\evil.exe", "evil.exe")]
    [InlineData("a:b*c.exe", "bc.exe")]
    [InlineData("..", "DigitalLogin-Setup.exe")]
    [InlineData("", "DigitalLogin-Setup.exe")]
    [InlineData(null, "DigitalLogin-Setup.exe")]
    public void Asset_names_become_safe_file_names(string? name, string expected)
    {
        Assert.Equal(expected, UpdateDownload.SafeFileName(name));
    }
}

public class InstallLocationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "digitallogin-install-tests", Guid.NewGuid().ToString("N"));

    public InstallLocationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_directory_with_an_inno_uninstaller_is_a_setup_install()
    {
        File.WriteAllText(Path.Combine(_folder, "unins000.exe"), "");
        Assert.True(InstallLocation.IsSetupInstall(_folder));
    }

    [Fact]
    public void A_portable_copy_is_not_a_setup_install()
    {
        File.WriteAllText(Path.Combine(_folder, "DigitalLogin.Maui.exe"), "");
        Assert.False(InstallLocation.IsSetupInstall(_folder));
    }

    [Theory]
    [InlineData("unins0001.exe")]
    [InlineData("unins00.exe")]
    [InlineData("uninsabc.exe")]
    public void Other_uninstaller_names_do_not_count(string name)
    {
        File.WriteAllText(Path.Combine(_folder, name), "");
        Assert.False(InstallLocation.IsSetupInstall(_folder));
    }

    [Fact]
    public void Missing_or_empty_directories_are_not_setup_installs()
    {
        Assert.False(InstallLocation.IsSetupInstall(Path.Combine(_folder, "nope")));
        Assert.False(InstallLocation.IsSetupInstall(""));
        Assert.False(InstallLocation.IsSetupInstall(null));
    }
}

public class UpdateCheckerTests
{
    /// <summary>Answers every request with the same status and body, and counts them.</summary>
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private const string NewerRelease = """
        { "tag_name": "v0.3.0", "html_url": "https://github.com/gianpaoloi/DigitalLogin/releases/tag/v0.3.0",
          "assets": [ { "name": "DigitalLogin-Setup-0.3.0.exe", "browser_download_url": "https://github.com/a/s.exe" } ] }
        """;

    [Fact]
    public async Task A_newer_release_is_reported_as_available()
    {
        var checker = new UpdateChecker(handler: new FakeHandler(HttpStatusCode.OK, NewerRelease), currentVersion: "0.2.0");

        var result = await checker.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("v0.3.0", result.Release!.TagName);
    }

    [Fact]
    public async Task The_same_or_older_release_is_up_to_date()
    {
        var checker = new UpdateChecker(handler: new FakeHandler(HttpStatusCode.OK, NewerRelease), currentVersion: "0.3.0");

        Assert.Equal(UpdateStatus.UpToDate, (await checker.CheckAsync()).Status);
    }

    [Fact]
    public async Task A_missing_release_is_a_quiet_failure_not_an_exception()
    {
        var checker = new UpdateChecker(handler: new FakeHandler(HttpStatusCode.NotFound, ""), currentVersion: "0.2.0");

        var result = await checker.CheckAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("no published release", result.Message);
    }

    [Fact]
    public async Task A_rate_limit_is_reported_as_a_failure()
    {
        var checker = new UpdateChecker(handler: new FakeHandler(HttpStatusCode.Forbidden, ""), currentVersion: "0.2.0");

        Assert.Contains("rate limit", (await checker.CheckAsync()).Message);
    }

    [Fact]
    public async Task A_fresh_answer_is_reused_until_forced()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, NewerRelease);
        var checker = new UpdateChecker(handler: handler, currentVersion: "0.2.0");

        await checker.CheckAsync();
        await checker.CheckAsync();
        Assert.Equal(1, handler.Requests);

        await checker.CheckAsync(force: true);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task Disabled_checks_make_no_request()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, NewerRelease);
        var checker = new UpdateChecker(new UpdateOptions { Enabled = false }, handler, "0.2.0");

        Assert.Equal(UpdateStatus.Disabled, (await checker.CheckAsync()).Status);
        Assert.Equal(0, handler.Requests);
    }
}
