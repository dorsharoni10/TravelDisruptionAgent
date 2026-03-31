using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _auth;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IOptions<AuthOptions> authOptions,
        IWebHostEnvironment env,
        ILogger<AuthController> logger)
    {
        _auth = authOptions.Value;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Development-only: returns a signed JWT for the SPA. Requires <see cref="AuthOptions.AllowDevTokenEndpoint"/> and Development environment.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("dev-token")]
    [EnableRateLimiting("auth")]
    public IActionResult IssueDevToken([FromBody] DevTokenRequest? body)
    {
        // Never issue dev tokens in Production, regardless of other flags.
        if (_env.IsProduction())
            return NotFound(new { error = "Dev token is not available in production." });

        if (!_auth.Enabled)
            return NotFound(new { error = "Authentication is disabled (Auth:Enabled=false)." });

        if (!_env.IsDevelopment() || !_auth.AllowDevTokenEndpoint)
            return NotFound(new { error = "Dev token endpoint is not available." });

        var sub = body?.Sub?.Trim();
        if (string.IsNullOrEmpty(sub) || sub.Length > 256)
            return BadRequest(new { error = "sub is required (max 256 chars)." });

        var name = string.IsNullOrWhiteSpace(body?.Name) ? sub : body!.Name!.Trim();
        if (name.Length > 256)
            return BadRequest(new { error = "name too long." });

        var keyBytes = Encoding.UTF8.GetBytes(_auth.Jwt.SecretKey);
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(Math.Clamp(_auth.Jwt.AccessTokenExpiryMinutes, 5, 24 * 60));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, sub),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, sub),
            new(ClaimTypes.Name, name)
        };

        var token = new JwtSecurityToken(
            issuer: _auth.Jwt.Issuer,
            audience: _auth.Jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(token);
        var ttl = (int)(expires - now).TotalSeconds;

        _logger.LogInformation("Issued dev JWT for sub {Sub}", sub);

        return Ok(new DevTokenResponse(accessToken, "Bearer", ttl));
    }
}
