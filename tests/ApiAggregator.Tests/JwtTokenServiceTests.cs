using System.IdentityModel.Tokens.Jwt;
using System.Text;
using ApiAggregator.Api.Authentication;
using ApiAggregator.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApiAggregator.Tests;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options_ = new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningKey = "unit-test-signing-key-that-is-at-least-32-chars",
        ExpiryMinutes = 30
    };

    private static TokenValidationParameters Parameters(string key) => new()
    {
        ValidIssuer = "issuer",
        ValidAudience = "audience",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true
    };

    [Fact]
    public void Issued_token_is_valid_and_carries_subject_and_expiry()
    {
        var service = new JwtTokenService(Options.Create(Options_), TimeProvider.System);

        var token = service.CreateToken("alice");

        new JwtSecurityTokenHandler().ValidateToken(token.Value, Parameters(Options_.SigningKey), out var validated);
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal("alice", jwt.Subject);
        Assert.InRange(token.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(29), DateTimeOffset.UtcNow.AddMinutes(31));
    }

    [Fact]
    public void Token_signed_with_another_key_is_rejected()
    {
        var token = new JwtTokenService(Options.Create(Options_), TimeProvider.System).CreateToken("alice");

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token.Value, Parameters("a-completely-different-key-of-32-chars!!"), out _));
    }
}
