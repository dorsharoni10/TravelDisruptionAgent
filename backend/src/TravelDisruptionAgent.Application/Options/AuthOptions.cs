namespace TravelDisruptionAgent.Application.Options;

/// <summary>API authentication. When <see cref="Enabled"/> is false, the API stays anonymous-friendly for local demos.</summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>When true, JWT Bearer is required for chat and preferences (except dev token and health).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When true and the host environment is Development, exposes POST /api/auth/dev-token to mint HS256 JWTs.
    /// Never enable in production.
    /// </summary>
    public bool AllowDevTokenEndpoint { get; set; }

    public JwtOptions Jwt { get; set; } = new();
}

public class JwtOptions
{
    /// <summary>Symmetric signing key (use env <c>Auth__Jwt__SecretKey</c>). Minimum 32 characters when <see cref="AuthOptions.Enabled"/> is true.</summary>
    public string SecretKey { get; set; } = "";

    public string Issuer { get; set; } = "TravelDisruptionAgent";

    public string Audience { get; set; } = "TravelDisruptionAgent.Spa";

    public int AccessTokenExpiryMinutes { get; set; } = 120;
}
