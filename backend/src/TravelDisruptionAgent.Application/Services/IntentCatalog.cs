using TravelDisruptionAgent.Application.Constants;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>
/// Maps granular intents to legacy workflow intents used by planning, validation, and guardrails.
/// </summary>
public static class IntentCatalog
{
    public static string ToWorkflowIntent(string granularIntent, string userMessage)
    {
        var m = userMessage.ToLowerInvariant();

        if (IsLegacyIntent(granularIntent))
            return granularIntent;

        return granularIntent switch
        {
            AgentIntents.MixedDisruption => InferDisruptionWorkflow(m),
            AgentIntents.DisruptionAssistance => InferDisruptionWorkflow(m),
            AgentIntents.FlightOperational => OperationalFlightWorkflow(m),
            AgentIntents.WeatherOperational => "weather_disruption",
            AgentIntents.CompanyTravelPolicy or AgentIntents.ReimbursementExpenses => "general_travel_disruption",
            AgentIntents.BaggageCheckinPolicy => "baggage_issue",
            AgentIntents.RebookingPolicy => "rebooking_request",
            AgentIntents.MissedConnection => "missed_connection",
            AgentIntents.StrikeDisruption => "strike_disruption",
            AgentIntents.FlightDiversion => "flight_diversion",
            AgentIntents.OutOfScope => "out_of_scope",
            _ => "general_travel_disruption"
        };
    }

    private static bool IsLegacyIntent(string intent) =>
        intent is "flight_cancellation" or "flight_delay" or "weather_disruption"
            or "missed_connection" or "rebooking_request" or "baggage_issue"
            or "strike_disruption" or "flight_diversion" or "general_travel_disruption"
            or "unknown" or "out_of_scope";

    private static string InferDisruptionWorkflow(string m)
    {
        if (ContainsCancel(m)) return "flight_cancellation";
        if (ContainsMissedConnection(m)) return "missed_connection";
        if (ContainsDelay(m)) return "flight_delay";
        if (m.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("storm", StringComparison.OrdinalIgnoreCase))
            return "weather_disruption";
        return "general_travel_disruption";
    }

    /// <summary>Status / gate / ETA queries without explicit disruption wording.</summary>
    private static string OperationalFlightWorkflow(string m)
    {
        if (ContainsCancel(m) || ContainsDelay(m) || ContainsMissedConnection(m))
            return InferDisruptionWorkflow(m);
        return "flight_delay";
    }

    private static bool ContainsCancel(string m) =>
        m.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("cancellation", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDelay(string m) =>
        m.Contains("delay", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("delayed", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsMissedConnection(string m) =>
        (m.Contains("missed", StringComparison.OrdinalIgnoreCase) &&
         (m.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
          m.Contains("connection", StringComparison.OrdinalIgnoreCase))) ||
        m.Contains("missed connection", StringComparison.OrdinalIgnoreCase);

    public static bool GranularIntentImpliesTools(string intent) =>
        intent is AgentIntents.FlightOperational or AgentIntents.WeatherOperational
            or AgentIntents.DisruptionAssistance or AgentIntents.MixedDisruption
            or AgentIntents.MissedConnection or AgentIntents.FlightDiversion
            or AgentIntents.StrikeDisruption;

    public static bool GranularIntentImpliesRag(string intent) =>
        intent is AgentIntents.CompanyTravelPolicy or AgentIntents.ReimbursementExpenses
            or AgentIntents.BaggageCheckinPolicy or AgentIntents.RebookingPolicy
            or AgentIntents.MixedDisruption or AgentIntents.DisruptionAssistance;
}
