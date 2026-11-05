using DigitalLogin.Auth;
using Microsoft.Data.Sqlite;

namespace DigitalLogin.Services;

/// <summary>One finished login test, kept for the History page. Holds no secret and no token.</summary>
public sealed record LoginRunRecord(
    DateTimeOffset StartedAt,
    string BaseUrl,
    string CompanyId,
    string CompanyVat,
    string RequestId,
    bool Succeeded,
    string? FailedStep,
    int? FailedHttpStatus,
    TimeSpan Duration,
    string? Error)
{
    public static LoginRunRecord From(AgyoOptions options, AgyoLoginRun run) => new(
        run.StartedAt,
        options.BaseUrl,
        options.Id,
        options.CompanyVat,
        run.RequestId,
        run.Succeeded,
        run.FailedStep,
        run.FailedHttpStatus,
        run.Duration,
        run.Error);
}

/// <summary>
/// Login run history persisted in a SQLite file, newest first. Keeps the most recent
/// <see cref="Capacity"/> runs. The token is never written to the database.
/// </summary>
public sealed class LoginRunHistory : IDisposable
{
    public const int Capacity = 200;

    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS LoginRuns (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            StartedAt       TEXT    NOT NULL,
            BaseUrl         TEXT    NOT NULL,
            CompanyId       TEXT    NOT NULL,
            CompanyVat      TEXT    NOT NULL,
            RequestId       TEXT    NOT NULL,
            Succeeded       INTEGER NOT NULL,
            FailedStep      TEXT    NULL,
            FailedHttpStatus INTEGER NULL,
            DurationMs      REAL    NOT NULL,
            Error           TEXT    NULL
        );
        """;

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;

    /// <param name="databasePath">Path to the SQLite file. Its folder is created if missing.</param>
    public LoginRunHistory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString());
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = CreateTable;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Raised after <see cref="Add"/> or <see cref="Clear"/>, on the thread that made the change.</summary>
    public event Action? Changed;

    /// <summary>All stored runs, newest first.</summary>
    public IReadOnlyList<LoginRunRecord> Runs
    {
        get
        {
            lock (_gate)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = """
                    SELECT StartedAt, BaseUrl, CompanyId, CompanyVat, RequestId, Succeeded,
                           FailedStep, FailedHttpStatus, DurationMs, Error
                    FROM LoginRuns
                    ORDER BY Id DESC
                    LIMIT $limit;
                    """;
                cmd.Parameters.AddWithValue("$limit", Capacity);

                var list = new List<LoginRunRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new LoginRunRecord(
                        DateTimeOffset.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetInt64(5) != 0,
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7),
                        TimeSpan.FromMilliseconds(reader.GetDouble(8)),
                        reader.IsDBNull(9) ? null : reader.GetString(9)));
                }

                return list;
            }
        }
    }

    public void Add(LoginRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();

            using (var insert = _connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO LoginRuns (StartedAt, BaseUrl, CompanyId, CompanyVat, RequestId, Succeeded,
                                           FailedStep, FailedHttpStatus, DurationMs, Error)
                    VALUES ($startedAt, $baseUrl, $companyId, $companyVat, $requestId, $succeeded,
                            $failedStep, $failedHttpStatus, $durationMs, $error);
                    """;
                insert.Parameters.AddWithValue("$startedAt", record.StartedAt.ToString("O"));
                insert.Parameters.AddWithValue("$baseUrl", record.BaseUrl);
                insert.Parameters.AddWithValue("$companyId", record.CompanyId);
                insert.Parameters.AddWithValue("$companyVat", record.CompanyVat);
                insert.Parameters.AddWithValue("$requestId", record.RequestId);
                insert.Parameters.AddWithValue("$succeeded", record.Succeeded ? 1 : 0);
                insert.Parameters.AddWithValue("$failedStep", (object?)record.FailedStep ?? DBNull.Value);
                insert.Parameters.AddWithValue("$failedHttpStatus", (object?)record.FailedHttpStatus ?? DBNull.Value);
                insert.Parameters.AddWithValue("$durationMs", record.Duration.TotalMilliseconds);
                insert.Parameters.AddWithValue("$error", (object?)record.Error ?? DBNull.Value);
                insert.ExecuteNonQuery();
            }

            // Trim to the newest Capacity rows.
            using (var trim = _connection.CreateCommand())
            {
                trim.Transaction = tx;
                trim.CommandText = """
                    DELETE FROM LoginRuns
                    WHERE Id NOT IN (SELECT Id FROM LoginRuns ORDER BY Id DESC LIMIT $capacity);
                    """;
                trim.Parameters.AddWithValue("$capacity", Capacity);
                trim.ExecuteNonQuery();
            }

            tx.Commit();
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM LoginRuns;";
            cmd.ExecuteNonQuery();
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) _connection.Dispose();
    }
}
