using System.ComponentModel.DataAnnotations;
using TravelDisruptionAgent.Application.Validation;

namespace TravelDisruptionAgent.Application.DTOs;

public record UserPreferencesDto(
    [property: MaxLength(PreferencesInputLimits.MaxStringFieldLength)]
    string PreferredAirline,
    [property: MaxLength(PreferencesInputLimits.MaxStringFieldLength)]
    string SeatPreference,
    [property: MaxLength(PreferencesInputLimits.MaxStringFieldLength)]
    string MealPreference,
    [property: MaxLength(PreferencesInputLimits.MaxStringFieldLength)]
    string LoyaltyProgram,
    [property: Range(0, PreferencesInputLimits.MaxLayovers)]
    int MaxLayovers,
    [property: Range(typeof(decimal), "0", PreferencesInputLimits.MaxBudgetUsdInvariant)]
    decimal MaxBudgetUsd,
    [property: MaxLength(PreferencesInputLimits.MaxPreferredAirportsCount)]
    List<string>? PreferredAirports,
    [property: MaxLength(PreferencesInputLimits.MaxAirportCodeLength)]
    string HomeAirport = "",
    [property: MaxLength(32)]
    string RiskTolerance = "moderate",
    bool PrefersRemoteFallback = false,
    [property: Range(0, PreferencesInputLimits.MaxMeetingBufferMinutes)]
    int PreferredMeetingBufferMinutes = 60
);
