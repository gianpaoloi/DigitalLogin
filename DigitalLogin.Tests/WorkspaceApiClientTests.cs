using System.Net;
using System.Text;
using DigitalLogin.Http;
using DigitalLogin.Workspace;
using Xunit;

namespace DigitalLogin.Tests;

public class WorkspaceApiClientTests
{
    private const string BaseUrl = "https://workspace-read.agyo.io";
    private const string WorkspaceId = "a6894675-5257-4a9e-9b51-fa735d3bbbf0";

    private const string WorkspaceJson = """
        {"id":"a6894675-5257-4a9e-9b51-fa735d3bbbf0","itemId":"item-1","ownerId":"owner-1",
         "classifier":"COMPANY","status":"ACTIVE","deleted":false,"taxIdentifier":"12345678901",
         "countryCode":"IT","baseCurrency":"EUR","businessLegalForm":"SRL",
         "establishmentDate":"2020-01-01T00:00:00Z","revision":"7"}
        """;

    private static WorkspaceRequest Request(string token = "tok-abc") => new()
    {
        BaseUrl = BaseUrl,
        AccessToken = token,
        WorkspaceId = WorkspaceId,
        UserId = "Company-1",
        UserAgent = "DigitalLogin-Test/1.0",
        AppName = "DigitalLogin",
        AppVersion = "9.9.9",
    };

    [Fact]
    public async Task Get_sends_bearer_token_and_workspace_headers_and_parses_the_fields()
    {
        var handler = new StubHandler(HttpStatusCode.OK, WorkspaceJson);
        var client = new WorkspaceApiClient(new HttpClient(handler));

        var result = await client.GetWorkspaceAsync(Request());

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(BaseUrl + "/api/v2/workspaces/" + WorkspaceId, handler.Url);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("tok-abc", handler.Authorization?.Parameter);
        Assert.Equal("Company-1", handler.Headers["X-User-ID"]);
        Assert.Equal(WorkspaceId, handler.Headers["X-Workspace-ID"]);
        Assert.Equal("DigitalLogin", handler.Headers["X-App-Name"]);
        Assert.Equal("9.9.9", handler.Headers["X-App-Version"]);
        Assert.Contains("X-Request-ID", handler.Headers.Keys);
        Assert.Contains("X-Correlation-ID", handler.Headers.Keys);

        Assert.Equal(WorkspaceId, result.Workspace.Id);
        Assert.Equal("owner-1", result.Workspace.OwnerId);
        Assert.Equal("COMPANY", result.Workspace.Classifier);
        Assert.Equal("ACTIVE", result.Workspace.Status);
        Assert.False(result.Workspace.Deleted);
        Assert.Equal("EUR", result.Workspace.BaseCurrency);
        Assert.Equal("2020-01-01T00:00:00Z", result.Workspace.EstablishmentDate);
        Assert.Null(result.Workspace.ExpiryDate);
        Assert.Equal(200, result.HttpStatus);
        Assert.Contains("\"revision\": \"7\"", result.PrettyJson);
    }

    [Fact]
    public async Task Non_success_status_throws_with_the_service_message()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"code":"401","message":"Unauthorized"}""");
        var client = new WorkspaceApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<AgyoApiException>(() => client.GetWorkspaceAsync(Request()));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("Unauthorized", ex.Message);
    }

    [Fact]
    public async Task Missing_token_is_rejected_before_any_request()
    {
        var handler = new StubHandler(HttpStatusCode.OK, WorkspaceJson);
        var client = new WorkspaceApiClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetWorkspaceAsync(Request(token: " ")));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Find_by_vat_returns_only_the_workspaces_with_that_vat()
    {
        const string list = """
            [{"id":"ws-1","itemId":"item-1","vatIdentifier":"01401810401","displayName":"ACME"},
             {"id":"ws-2","itemId":"item-2","vatIdentifier":"99999999999","displayName":"Other"},
             {"id":"ws-3","itemId":"item-3","displayName":"No VAT"}]
            """;
        var handler = new StubHandler(HttpStatusCode.OK, list);
        var client = new WorkspaceApiClient(new HttpClient(handler));

        var result = await client.FindByVatAsync(new MyWorkspacesRequest
        {
            BaseUrl = BaseUrl,
            AccessToken = "tok-abc",
            VatNumber = " 01401810401 ",
            UserId = "Company-1",
        });

        Assert.Equal(BaseUrl + "/api/v2/my-workspaces", handler.Url);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        var match = Assert.Single(result.Matches);
        Assert.Equal("ws-1", match.Id);
        Assert.Equal("ACME", match.DisplayName);
    }

    [Fact]
    public async Task Find_by_vat_with_no_match_returns_an_empty_list()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """[{"id":"ws-2","vatIdentifier":"99999999999"}]""");
        var client = new WorkspaceApiClient(new HttpClient(handler));

        var result = await client.FindByVatAsync(new MyWorkspacesRequest
        {
            BaseUrl = BaseUrl,
            AccessToken = "tok-abc",
            VatNumber = "01401810401",
            UserId = "Company-1",
        });

        Assert.Empty(result.Matches);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Url { get; private set; }
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            Method = request.Method;
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization;
            foreach (var h in request.Headers)
                Headers[h.Key] = string.Join(",", h.Value);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
