using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using ApiAggregator.Api.Authentication;
using ApiAggregator.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Api.Controllers;

public sealed class LoginRequest
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class AuthController(IOptions<AuthOptions> auth, IJwtTokenService tokens) : ControllerBase
{
    /// <summary>Exchanges the demo credentials (see appsettings "Auth") for a JWT bearer token.</summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult CreateToken([FromBody] LoginRequest request)
    {
        var expected = auth.Value;
        var valid = FixedTimeEquals(request.Username, expected.Username) & FixedTimeEquals(request.Password, expected.Password);

        if (!valid)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials");

        var token = tokens.CreateToken(request.Username);
        return Ok(new TokenResponse(token.Value, "Bearer", token.ExpiresAt));
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
