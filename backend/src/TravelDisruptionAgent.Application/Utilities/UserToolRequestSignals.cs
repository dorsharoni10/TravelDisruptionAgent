using System.Text.RegularExpressions;

namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>
/// Detects when the user explicitly asked for optional tool use (alternatives / route browse / weather).
/// Used to avoid running or recommending route search and weather unless requested.
/// Hebrew substrings match Hebrew user input; all documentation is in English.
/// </summary>
public static class UserToolRequestSignals
{
    public static bool ExplicitlyAsksForWeather(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var m = message.ToLowerInvariant();
        if (m.Contains("מזג אוויר", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("תחזית", StringComparison.OrdinalIgnoreCase))
            return true;

        return m.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("forecast", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("temperature", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("storm", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("snow", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("windy", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("visibility", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("conditions at", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("conditions in", StringComparison.OrdinalIgnoreCase) ||
               (m.Contains("conditions", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(message, @"\b[A-Z]{3}\b", RegexOptions.IgnoreCase));
    }

    /// <summary>User wants other flights, rebooking, or an explicit route/flight search (not only status of one flight).</summary>
    public static bool ExplicitlyAsksForRouteOrAlternatives(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var m = message.ToLowerInvariant();

        if (m.Contains("טיסה חלופית", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("טיסות חלופיות", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("להחליף טיסה", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("חלופי", StringComparison.OrdinalIgnoreCase) && m.Contains("טיס", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("rebook", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("re-book", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("rebooking", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("alternative flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("alternatives", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("other flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("other flights", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("another flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("different flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("switch to a different flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("change my flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("replace my flight", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("backup flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("plan b", StringComparison.OrdinalIgnoreCase))
            return true;

        // Explicit route / availability browse (not just mentioning two airport codes).
        if ((m.Contains("search for flight", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("find flight", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("available flight", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("flights available", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("what flights", StringComparison.OrdinalIgnoreCase)) &&
            (m.Contains(" from ", StringComparison.OrdinalIgnoreCase) ||
             m.Contains(" to ", StringComparison.OrdinalIgnoreCase) ||
             m.Contains(" tlv", StringComparison.OrdinalIgnoreCase) ||
             Regex.IsMatch(message, @"\b[A-Z]{3}\b.*\b[A-Z]{3}\b", RegexOptions.IgnoreCase)))
            return true;

        if (m.Contains("missed connection", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("missed my connection", StringComparison.OrdinalIgnoreCase))
            return true;

        // After a clear disruption, asking for options / next steps implies rebooking and alternatives.
        if (MessageImpliesDisruptionScenario(m))
        {
            if (m.Contains("what are my options", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("what are your options", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("what can i do", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("what should i do", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("what do i do", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("help me rebook", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("need to get home", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("how do i get there", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("מה האפשרויות", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("מה אפשר לעשות", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("מה לעשות", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool MessageImpliesDisruptionScenario(string m) =>
        m.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("scrubbed", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("grounded", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("bumped", StringComparison.OrdinalIgnoreCase) ||
        (m.Contains("delay", StringComparison.OrdinalIgnoreCase) && m.Contains("flight", StringComparison.OrdinalIgnoreCase)) ||
        m.Contains("missed my flight", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("missed connection", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("disrupt", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("בוטל", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("ביטול", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("עיכוב", StringComparison.OrdinalIgnoreCase) ||
        m.Contains("פספסתי", StringComparison.OrdinalIgnoreCase);
}
