namespace DigitalLogin.Environments;

/// <summary>
/// An Agyo environment: the auth service used for login, the Workspace Read API used for workspaces and
/// the Registry Read API used to find a company by VAT number.
/// </summary>
public sealed record AgyoEnvironment(string Name, string LoginBaseUrl, string WorkspaceBaseUrl, string RegistryBaseUrl)
{
    public static readonly AgyoEnvironment Prod = new(
        "PROD",
        "https://b2b-auth-service.agyo.io",
        "https://workspace-read.agyo.io",
        "https://registry-read.agyo.io");

    public static readonly AgyoEnvironment Stage = new(
        "STAGE",
        "https://b2b-auth-service-test.agyo.io",
        "https://workspace-read-test.agyo.io",
        "https://registry-read-test.agyo.io");

    public static IReadOnlyList<AgyoEnvironment> All { get; } = [Prod, Stage];
}
