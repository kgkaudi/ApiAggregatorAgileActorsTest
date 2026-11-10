using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiAggregator.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApiAggregator.Tests;

public class ApiIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new { username = "demo", password = "demo-password" });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/aggregation?query=dotnet")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/stats")).StatusCode);
    }

    [Fact]
    public async Task Token_endpoint_rejects_wrong_credentials()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/token", new { username = "demo", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stats_endpoint_is_available_with_a_valid_token()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("thresholds", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_parameters_return_400_before_any_external_call_is_made()
    {
        var client = await AuthenticatedClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/aggregation")).StatusCode);                          // query missing
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/aggregation?query=dotnet&sources=Nope")).StatusCode);  // unknown source
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/aggregation?query=dotnet&pageSize=1000")).StatusCode);
    }
}
