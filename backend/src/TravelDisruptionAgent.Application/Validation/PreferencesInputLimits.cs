using System.ComponentModel.DataAnnotations;

namespace TravelDisruptionAgent.Application.Validation;

/// <summary>Bounds for persisted user preferences (storage + prompt injection surface).</summary>
public static class PreferencesInputLimits
{
    public const int MaxStringFieldLength = 256;
    public const int MaxAirportCodeLength = 4;
    public const int MaxPreferredAirportsCount = 50;
    public const int MaxLayovers = 10;
    public const decimal MaxBudgetUsd = 500_000m;

    /// <summary>Upper bound for <see cref="RangeAttribute"/> on <c>decimal</c> (must match <see cref="MaxBudgetUsd"/>).</summary>
    public const string MaxBudgetUsdInvariant = "500000";

    public const int MaxMeetingBufferMinutes = 24 * 60;
}
