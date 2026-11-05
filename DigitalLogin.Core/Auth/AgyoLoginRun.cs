namespace DigitalLogin.Auth;

public enum AgyoStepStatus { Pending, Running, Succeeded, Failed, Skipped }

/// <summary>One request or local computation in the login flow, as shown in the UI.</summary>
public sealed class AgyoStep
{
    public required string Name { get; init; }
    public string Method { get; init; } = "";
    public string Url { get; set; } = "";

    public AgyoStepStatus Status { get; set; } = AgyoStepStatus.Pending;
    public int? HttpStatus { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>Short human-readable result or error. Never contains the secret or the full token.</summary>
    public string? Detail { get; set; }

    /// <summary>Response body with the access token redacted. Null for local steps.</summary>
    public string? ResponseBody { get; set; }
}

/// <summary>
/// The state of one login test. Created by the caller and filled in by
/// <see cref="AgyoAuthClient.RunAsync"/>, so the UI can render progress while the run is in flight.
/// </summary>
public sealed class AgyoLoginRun
{
    public AgyoLoginRun()
    {
        var guid = Guid.NewGuid();
        // Same identifiers the Bruno script derives: requestId is the GUID without dashes,
        // correlationId is the GUID as-is. Both are reused across the two calls.
        RequestId = guid.ToString("N");
        CorrelationId = guid.ToString();
    }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public string RequestId { get; }
    public string CorrelationId { get; }

    public List<AgyoStep> Steps { get; } =
    [
        new AgyoStep { Name = "Request nonce", Method = "POST", Url = "/api/v3/nonces" },
        new AgyoStep { Name = "Compute digest", Method = "local", Url = "SHA256(SHA256(id + secret) + nonce)" },
        new AgyoStep { Name = "Request access token", Method = "POST", Url = "/api/v3/tokens" },
    ];

    public bool Finished { get; set; }
    public bool Succeeded { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>Full token, kept in memory for the current session only. Never written to history.</summary>
    public string? AccessToken { get; set; }

    public string? Error { get; set; }
    public string? FailedStep { get; set; }
    public int? FailedHttpStatus { get; set; }
}
