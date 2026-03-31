namespace TravelDisruptionAgent.Application.Constants;

/// <summary>Primary user-intent taxonomy for routing, RAG topic hints, and telemetry.</summary>
public static class AgentIntents
{
    public const string FlightOperational = "flight_operational";
    public const string WeatherOperational = "weather_operational";
    public const string DisruptionAssistance = "disruption_assistance";
    public const string CompanyTravelPolicy = "company_travel_policy";
    public const string ReimbursementExpenses = "reimbursement_expenses";
    public const string BaggageCheckinPolicy = "baggage_checkin_policy";
    public const string RebookingPolicy = "rebooking_policy";
    public const string MissedConnection = "missed_connection";
    public const string StrikeDisruption = "strike_disruption";
    public const string FlightDiversion = "flight_diversion";
    /// <summary>Live data plus policy in one question.</summary>
    public const string MixedDisruption = "mixed_disruption";
    public const string OutOfScope = "out_of_scope";
}
