using DigitalLogin.Services;
using Xunit;

namespace DigitalLogin.Tests;

public class LoginRunHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "digitallogin-tests", Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_folder, "history.db");

    public void Dispose()
    {
        // Connections are released by each test; SQLite pooling is off, so the file can be deleted.
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static LoginRunRecord Record(int n, bool succeeded = true) => new(
        new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero).AddSeconds(n),
        "https://b2b-auth-service-test.agyo.io",
        $"company-{n}",
        "IT00000000001",
        Guid.NewGuid().ToString("N"),
        succeeded,
        succeeded ? null : "Request nonce",
        succeeded ? null : 401,
        TimeSpan.FromMilliseconds(120 + n),
        succeeded ? null : "HTTP 401 Unauthorized: {}");

    [Fact]
    public void Add_then_read_returns_newest_first_with_all_fields()
    {
        using var history = new LoginRunHistory(DbPath);

        history.Add(Record(1));
        history.Add(Record(2, succeeded: false));

        var runs = history.Runs;
        Assert.Equal(2, runs.Count);
        Assert.Equal("company-2", runs[0].CompanyId);
        Assert.False(runs[0].Succeeded);
        Assert.Equal("Request nonce", runs[0].FailedStep);
        Assert.Equal(401, runs[0].FailedHttpStatus);
        Assert.Equal("HTTP 401 Unauthorized: {}", runs[0].Error);

        var first = runs[1];
        Assert.Equal(Record(1).StartedAt, first.StartedAt);
        Assert.True(first.Succeeded);
        Assert.Null(first.FailedStep);
        Assert.Null(first.FailedHttpStatus);
        Assert.Null(first.Error);
        Assert.Equal(TimeSpan.FromMilliseconds(121), first.Duration);
    }

    [Fact]
    public void Runs_survive_reopening_the_database()
    {
        using (var history = new LoginRunHistory(DbPath))
        {
            history.Add(Record(1));
            history.Add(Record(2));
        }

        using var reopened = new LoginRunHistory(DbPath);
        Assert.Equal(new[] { "company-2", "company-1" }, reopened.Runs.Select(r => r.CompanyId));
    }

    [Fact]
    public void History_keeps_only_the_newest_200_runs()
    {
        using var history = new LoginRunHistory(DbPath);

        for (var i = 1; i <= LoginRunHistory.Capacity + 5; i++)
            history.Add(Record(i));

        var runs = history.Runs;
        Assert.Equal(LoginRunHistory.Capacity, runs.Count);
        Assert.Equal($"company-{LoginRunHistory.Capacity + 5}", runs[0].CompanyId);
        Assert.Equal("company-6", runs[^1].CompanyId);
    }

    [Fact]
    public void Clear_removes_everything_and_persists_the_empty_state()
    {
        using (var history = new LoginRunHistory(DbPath))
        {
            history.Add(Record(1));
            history.Clear();
            Assert.Empty(history.Runs);
        }

        using var reopened = new LoginRunHistory(DbPath);
        Assert.Empty(reopened.Runs);
    }

    [Fact]
    public void Changed_is_raised_on_add_and_clear()
    {
        using var history = new LoginRunHistory(DbPath);
        var raised = 0;
        history.Changed += () => raised++;

        history.Add(Record(1));
        history.Clear();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Creates_the_missing_folder()
    {
        Assert.False(Directory.Exists(_folder));

        using var history = new LoginRunHistory(DbPath);

        Assert.True(File.Exists(DbPath));
    }
}
