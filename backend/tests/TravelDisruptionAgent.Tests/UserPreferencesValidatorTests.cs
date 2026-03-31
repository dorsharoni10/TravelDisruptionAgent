using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Validation;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class UserPreferencesValidatorTests
{
    private static UserPreferencesDto ValidDto() =>
        new(
            PreferredAirline: "AA",
            SeatPreference: "window",
            MealPreference: "veg",
            LoyaltyProgram: "AAdvantage",
            MaxLayovers: 2,
            MaxBudgetUsd: 1000,
            PreferredAirports: ["JFK", "lax"],
            HomeAirport: "bos",
            RiskTolerance: "Moderate",
            PrefersRemoteFallback: false,
            PreferredMeetingBufferMinutes: 30);

    [Fact]
    public void GetErrors_empty_when_valid()
    {
        var errors = UserPreferencesValidator.GetErrors(ValidDto());
        Assert.Empty(errors);
    }

    [Fact]
    public void GetErrors_rejects_invalid_risk()
    {
        var dto = ValidDto() with { RiskTolerance = "extreme" };
        var errors = UserPreferencesValidator.GetErrors(dto);
        Assert.Contains("RiskTolerance", errors.Keys);
    }

    [Fact]
    public void GetErrors_rejects_bad_airport_entry()
    {
        var dto = ValidDto() with { PreferredAirports = ["JFK", "NOPE!"] };
        var errors = UserPreferencesValidator.GetErrors(dto);
        Assert.Contains("PreferredAirports", errors.Keys);
    }

    [Fact]
    public void NormalizeRiskTolerance_lowercases()
    {
        Assert.Equal("safe", UserPreferencesValidator.NormalizeRiskTolerance(" Safe "));
    }
}
