namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>
/// Heuristics for follow-ups that likely refer to flight data from an earlier turn in the same session.
/// </summary>
public static class HistoryFollowUpSignals
{
    public static bool LikelyReferencesPriorFlightDetail(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var t = userMessage.ToLowerInvariant();
        if (t.Contains("alternative flight", StringComparison.Ordinal) ||
            t.Contains("alternative you", StringComparison.Ordinal) ||
            t.Contains("the alternative", StringComparison.Ordinal))
            return true;
        if (t.Contains("you found", StringComparison.Ordinal) ||
            t.Contains("you mentioned", StringComparison.Ordinal) ||
            t.Contains("earlier you", StringComparison.Ordinal))
            return true;
        if (t.Contains("arrival time", StringComparison.Ordinal) ||
            t.Contains("departure time", StringComparison.Ordinal))
            return true;
        if (t.Contains("that flight", StringComparison.Ordinal) ||
            t.Contains("same flight", StringComparison.Ordinal))
            return true;

        return false;
    }
}
