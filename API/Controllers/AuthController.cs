using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace API.Controllers;

public record LoginRequest(string Username, string Password);

[ApiVersion(2)]
[ApiController]
[Route("v{v:apiVersion}/[controller]")]
public class AuthController() : ControllerBase
{
    internal const string CookieName = "tranga_auth";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether a username/password is required to use the API. Always anonymous - the frontend needs
    /// this before it knows whether to show a login page at all.
    /// </summary>
    [HttpGet("Enabled")]
    [AllowAnonymous]
    [ProducesResponseType<bool>(Status200OK, "text/plain")]
    public Ok<bool> GetAuthEnabled() => TypedResults.Ok(Constants.AuthEnabled);

    /// <summary>
    /// Logs in with the username/password configured via AUTH_USERNAME/AUTH_PASSWORD, setting an
    /// HttpOnly session cookie on success.
    /// </summary>
    [HttpPost("Login")]
    [AllowAnonymous]
    [ProducesResponseType(Status200OK)]
    [ProducesResponseType(Status401Unauthorized)]
    public IResult Login([FromBody] LoginRequest request)
    {
        if (!Constants.AuthEnabled)
            return TypedResults.Ok();

        bool validUsername = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(request.Username), Encoding.UTF8.GetBytes(Constants.AuthUsername!));
        bool validPassword = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(request.Password), Encoding.UTF8.GetBytes(Constants.AuthPassword!));
        if (!validUsername || !validPassword)
            return TypedResults.Unauthorized();

        JwtSecurityTokenHandler handler = new();
        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity([new Claim(ClaimTypes.Name, request.Username)]),
            Expires = DateTime.UtcNow.Add(SessionLifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Auth.AuthKeyProvider.GetOrCreateSigningKey()), SecurityAlgorithms.HmacSha256)
        };
        string token = handler.WriteToken(handler.CreateToken(descriptor));

        Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(SessionLifetime)
        });
        return TypedResults.Ok();
    }

    /// <summary>
    /// Returns 200 if the caller has a valid session, 401 otherwise (enforced by the FallbackPolicy).
    /// Used by the frontend to decide whether to show the login page.
    /// </summary>
    [HttpGet("Session")]
    [ProducesResponseType(Status200OK)]
    public Ok GetSession() => TypedResults.Ok();

    [HttpPost("Logout")]
    [AllowAnonymous]
    [ProducesResponseType(Status200OK)]
    public Ok Logout()
    {
        Response.Cookies.Delete(CookieName);
        return TypedResults.Ok();
    }
}
