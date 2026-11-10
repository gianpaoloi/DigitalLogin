using System.Net;
using System.Text;
using DigitalLogin.Http;
using DigitalLogin.CompanyRegistry;
using Xunit;

namespace DigitalLogin.Tests;

public class RegistryApiClientTests
{
    private const string BaseUrl = "https://registry-read-test.agyo.io";

    private const string TwoItemsJson = """
        {"items":[
          {"item":{"base":{"id":"80e1e868-9e1c-4aae-8587-933360f2aade",
             "identifier":{"taxId":"12345678901","taxRegion":"IT","vatNumber":"12345678901"},
             "details":{"classifier":"COMPANY","description":"ACME SRL","legalForm":{"code":"SRL","description":"Societa a responsabilita limitata"},
                        "addresses":[{"id":"a1","fullAddress":"Via Roma 12, Milano (MI) 20100 - Italy"}]},
             "status":{"active":true,"deleted":false}}}},
          {"item":{"base":{"id":"11111111-2222-3333-4444-555555555555",
             "identifier":{"vatNumber":"12345678901"},
             "details":{"classifier":"PERSON","firstName":"Mario","lastName":"Rossi","addresses":[]},
             "status":{"active":false,"deleted":true}}}}
        ],"totalItems":2}
        """;

    private static RegistryRequest Request(string vat = " 12345678901 ", string token = "tok-abc") => new()
    {
        BaseUrl = BaseUrl,
        AccessToken = token,
        VatNumber = vat,
        UserAgent = "DigitalLogin-Test/1.0",
        AppName = "DigitalLogin",
        AppVersion = "9.9.9",
    };

    [Fact]
    public async Task Lookup_calls_items_by_vat_with_bearer_token_and_parses_the_items()
    {
        var handler = new StubHandler(HttpStatusCode.OK, TwoItemsJson);
        var client = new RegistryApiClient(new HttpClient(handler));

        var result = await client.FindByVatAsync(Request());

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(BaseUrl + "/api/v3/items?identifier.taxRegion=IT&identifier.vatNumber=12345678901&packageType=BASE&pagination.pageNumber=0&pagination.itemsPerPage=100", handler.Url);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("tok-abc", handler.Authorization?.Parameter);
        Assert.Equal("DigitalLogin", handler.Headers["X-App-Name"]);
        Assert.Contains("X-Request-ID", handler.Headers.Keys);

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.Items.Count);

        var first = result.Items[0];
        Assert.Equal("80e1e868-9e1c-4aae-8587-933360f2aade", first.Id);
        Assert.Equal("ACME SRL", first.Description);
        Assert.Equal("COMPANY", first.Classifier);
        Assert.Equal("12345678901", first.VatNumber);
        Assert.Equal("IT", first.TaxRegion);
        Assert.Equal("Societa a responsabilita limitata", first.LegalForm);
        Assert.True(first.Active);
        Assert.False(first.Deleted);
        Assert.Equal(["Via Roma 12, Milano (MI) 20100 - Italy"], first.Addresses);

        var second = result.Items[1];
        Assert.Equal("Mario Rossi", second.Description);
        Assert.False(second.Active);
        Assert.True(second.Deleted);
        Assert.Empty(second.Addresses);
    }

    [Fact]
    public async Task Empty_result_gives_no_items()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"items":[],"totalItems":0}""");
        var client = new RegistryApiClient(new HttpClient(handler));

        var result = await client.FindByVatAsync(Request());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task Non_success_status_throws_with_the_service_message()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"code":"400","message":"packageType is required"}""");
        var client = new RegistryApiClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<AgyoApiException>(() => client.FindByVatAsync(Request()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("packageType is required", ex.Message);
    }

    [Fact]
    public async Task Missing_vat_is_rejected_before_any_request()
    {
        var handler = new StubHandler(HttpStatusCode.OK, TwoItemsJson);
        var client = new RegistryApiClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByVatAsync(Request(vat: "  ")));

        Assert.Equal(0, handler.CallCount);
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
