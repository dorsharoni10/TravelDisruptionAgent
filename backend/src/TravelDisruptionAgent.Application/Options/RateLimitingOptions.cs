namespace TravelDisruptionAgent.Application.Options;

/// <summary>Per-endpoint fixed-window rate limits (partitioned by authenticated user id or client IP).</summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int ChatPermitLimit { get; set; } = 30;

    public int ChatWindowMinutes { get; set; } = 1;

    public int PreferencesPermitLimit { get; set; } = 120;

    public int PreferencesWindowMinutes { get; set; } = 1;

    public int AuthTokenPermitLimit { get; set; } = 15;

    public int AuthTokenWindowMinutes { get; set; } = 1;
}
