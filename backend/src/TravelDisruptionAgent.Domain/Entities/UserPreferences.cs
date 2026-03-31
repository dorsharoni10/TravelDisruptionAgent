namespace TravelDisruptionAgent.Domain.Entities;

public class UserPreferences
{
    public string UserId { get; set; } = "default";
    public string PreferredAirline { get; set; } = string.Empty;
    public string SeatPreference { get; set; } = string.Empty;
    public string MealPreference { get; set; } = string.Empty;
    public string LoyaltyProgram { get; set; } = string.Empty;
    public int MaxLayovers { get; set; } = 1;
    public decimal MaxBudgetUsd { get; set; } = 5000;
    public List<string> PreferredAirports { get; set; } = [];
    public string HomeAirport { get; set; } = string.Empty;
    public string RiskTolerance { get; set; } = "moderate";
    public bool PrefersRemoteFallback { get; set; }
    public int PreferredMeetingBufferMinutes { get; set; } = 60;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
