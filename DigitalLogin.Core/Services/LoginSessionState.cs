using DigitalLogin.Environments;

namespace DigitalLogin.Services;

/// <summary>
/// State that lives for the whole app session, so it survives navigating between pages.
/// The secret is held in memory only: it is never written to disk or to the history database.
/// </summary>
public sealed class LoginSessionState
{
    /// <summary>The client secret typed on the login page. Cleared only when the app closes.</summary>
    public string Secret { get; set; } = "";

    /// <summary>Access token from the last successful login test, reused by the workspace and registry pages. In memory only.</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>The environment chosen for login, workspace and registry calls. Defaults to STAGE, as before.</summary>
    public AgyoEnvironment Environment { get; set; } = AgyoEnvironment.Stage;

    /// <summary>Company VAT (partita IVA) typed on the login page; the registry page looks the company up by it.</summary>
    public string CompanyVat { get; set; } = "";

    /// <summary>Company ID: the first registry item found for the VAT. Set automatically by the registry lookup.</summary>
    public string CompanyId { get; set; } = "";

    /// <summary>Workspace ID used by the workspace page. Filled from the registry lookup, or typed.</summary>
    public string WorkspaceId { get; set; } = "";
}
