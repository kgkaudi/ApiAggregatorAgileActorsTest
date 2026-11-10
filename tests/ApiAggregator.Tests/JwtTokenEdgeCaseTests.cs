using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ApiAggregator.Api.Authentication;
using ApiAggregator.Api.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace ApiAggregator.Tests;

public class JwtTokenEdgeCaseTests
{
    private const string Key = "unit-test-signing-key-that-is-at-least-32-chars";

    private static readonly JwtOptions Config = new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningKey = Key,
        ExpiryMinutes = 30
    };

    private static JwtTokenService ServiceAt(DateTimeOffset now) => new(Options.Create(Config), new FakeTimeProvider(now));

    private static TokenValidationParameters Parameters(string issuer = "issuer", string audience = "audience") => new()
    {
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    private static void Validate(string token, TokenValidationParameters parameters) =>
        new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);

    [Fact]
    public void An_expired_token_is_rejected()
    {
        var token = ServiceAt(DateTimeOffset.UtcNow.AddHours(-2)).CreateToken("alice");   // expired 90 minutes ago

        Assert.ThrowsAny<SecurityTokenExpiredException>(() => Validate(token.Value, Parameters()));
    }

    [Fact]
    public void A_token_that_is_not_valid_yet_is_rejected()
    {
        var token = ServiceAt(DateTimeOffset.UtcNow.AddHours(1)).CreateToken("alice");

        Assert.ThrowsAny<SecurityTokenNotYetValidException>(() => Validate(token.Value, Parameters()));
    }

    [Fact]
    public void A_token_for_another_issuer_is_rejected()
    {
        var token = ServiceAt(DateTimeOffset.UtcNow).CreateToken("alice");

        Assert.ThrowsAny<SecurityTokenInvalidIssuerException>(() => Validate(token.Value, Parameters(issuer: "someone-else")));
    }

    [Fact]
    public void A_token_for_another_audience_is_rejected()
    {
        var token = ServiceAt(DateTimeOffset.UtcNow).CreateToken("alice");

        Assert.ThrowsAny<SecurityTokenInvalidAudienceException>(() => Validate(token.Value, Parameters(audience: "someone-else")));
    }

    [Fact]
    public void A_token_whose_payload_was_swapped_is_rejected()
    {
        var alice = ServiceAt(DateTimeOffset.UtcNow).CreateToken("alice").Value.Split('.');
        var mallory = ServiceAt(DateTimeOffset.UtcNow).CreateToken("mallory").Value.Split('.');

        // mallory's header + payload, alice's signature
        var forged = $"{mallory[0]}.{mallory[1]}.{alice[2]}";

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(forged, Parameters()));
    }

    [Fact]
    public void An_unsigned_token_with_alg_none_is_rejected()
    {
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Base64UrlEncoder.Encode($$"""{"sub":"alice","iss":"issuer","aud":"audience","exp":{{exp}}}""");

        Assert.ThrowsAny<SecurityTokenException>(() => Validate($"{header}.{payload}.", Parameters()));
    }

    [Fact]
    public void Expiry_is_exactly_the_configured_lifetime_after_issue()
    {
        var issuedAt = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var token = ServiceAt(issuedAt).CreateToken("alice");

        Assert.Equal(issuedAt.AddMinutes(30), token.ExpiresAt);
        Assert.Equal(issuedAt.UtcDateTime.AddMinutes(30), new JwtSecurityTokenHandler().ReadJwtToken(token.Value).ValidTo);
    }

    [Fact]
    public void Every_token_has_a_unique_id()
    {
        var service = ServiceAt(DateTimeOffset.UtcNow);
        var handler = new JwtSecurityTokenHandler();

        var first = handler.ReadJwtToken(service.CreateToken("alice").Value).Id;
        var second = handler.ReadJwtToken(service.CreateToken("alice").Value).Id;

        Assert.NotEqual(first, second);
    }
}

/// <summary>The same JWT rules, checked through the real HTTP pipeline.</summary>
public class JwtApiEdgeCaseTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private JwtOptions AppJwt => factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    private HttpClient ClientWith(AuthenticationHeaderValue? authorization)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authorization;
        return client;
    }

    private string TokenFor(JwtOptions options, DateTimeOffset issuedAt) =>
        new JwtTokenService(Options.Create(options), new FakeTimeProvider(issuedAt)).CreateToken("demo").Value;

    private JwtOptions AppJwtWith(Action<JwtOptions> change)
    {
        var copy = new JwtOptions
        {
            Issuer = AppJwt.Issuer,
            Audience = AppJwt.Audience,
            SigningKey = AppJwt.SigningKey,
            ExpiryMinutes = AppJwt.ExpiryMinutes
        };
        change(copy);
        return copy;
    }

    [Fact]
    public async Task A_correctly_signed_current_token_is_accepted()
    {
        var token = TokenFor(AppJwt, DateTimeOffset.UtcNow);

        var response = await ClientWith(new("Bearer", token)).GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_gets_401()
    {
        var token = TokenFor(AppJwt, DateTimeOffset.UtcNow.AddHours(-3));   // app lifetime is 60 minutes

        var response = await ClientWith(new("Bearer", token)).GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_gets_401()
    {
        var token = TokenFor(AppJwtWith(o => o.SigningKey = "a-completely-different-key-of-32-chars!!"), DateTimeOffset.UtcNow);

        var response = await ClientWith(new("Bearer", token)).GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_audience_gets_401()
    {
        var token = TokenFor(AppJwtWith(o => o.Audience = "someone-else"), DateTimeOffset.UtcNow);

        var response = await ClientWith(new("Bearer", token)).GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    public async Task A_malformed_bearer_token_gets_401(string token)
    {
        var response = await ClientWith(new("Bearer", token)).GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_non_bearer_authorization_scheme_gets_401()
    {
        var response = await ClientWith(new("Basic", "ZGVtbzpkZW1vLXBhc3N3b3Jk")).GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"username":"demo"}""")]
    [InlineData("""{"password":"demo-password"}""")]
    public async Task The_token_endpoint_rejects_incomplete_credentials_with_400(string body)
    {
        var response = await factory.CreateClient()
            .PostAsync("/api/auth/token", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_token_endpoint_rejects_a_wrong_username_with_the_right_password()
    {
        var response = await factory.CreateClient().PostAsync("/api/auth/token",
            new StringContent("""{"username":"someone","password":"demo-password"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
