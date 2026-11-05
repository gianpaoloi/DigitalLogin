namespace DigitalLogin.Auth;

/// <summary>
/// Everything one login test needs. The secret lives here only for the duration of a run; it is
/// never persisted and never written to the step log.
/// </summary>
public sealed record AgyoOptions
{
    /// <summary>Base URL of the auth service, without a path (e.g. https://host).</summary>
    public string BaseUrl { get; init; } = "https://b2b-auth-service-test.agyo.io";

    /// <summary>Company id, sent as "id" in the body and as the X-User-ID header.</summary>
    public string Id { get; init; } = "";

    /// <summary>Shared secret used to build the digest.</summary>
    public string Secret { get; init; } = "";

    /// <summary>VAT number of the company. Informational: the Bruno script logs it, it is not sent.</summary>
    public string CompanyVat { get; init; } = "";

    public string UserAgent { get; init; } = "DigitalLogin/1.0";
    public string AppName { get; init; } = "DigitalLogin";
    public string AppVersion { get; init; } = "1.0.0";

    /// <summary>Keeps the secret out of logs and debugger output.</summary>
    public override string ToString() => $"AgyoOptions {{ BaseUrl = {BaseUrl}, Id = {Id}, CompanyVat = {CompanyVat} }}";
}
