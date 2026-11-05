namespace DigitalLogin.Services;

/// <summary>
/// State that lives for the whole app session, so it survives navigating between pages.
/// The secret is held in memory only: it is never written to disk or to the history database.
/// </summary>
public sealed class LoginSessionState
{
    /// <summary>The client secret typed on the login page. Cleared only when the app closes.</summary>
    public string Secret { get; set; } = "";
}
