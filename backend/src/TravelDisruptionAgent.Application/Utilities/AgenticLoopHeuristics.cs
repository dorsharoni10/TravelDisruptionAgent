namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>Deterministic helpers for stop rules and duplicate detection (unit-tested).</summary>
public static class AgenticLoopHeuristics
{
    public static string NormalizePolicyQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";
        var t = query.Trim().ToLowerInvariant();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t;
    }

    public static bool ObservationsContentEqual(string? a, string? b) =>
        string.Equals(NormalizeObservation(a), NormalizeObservation(b), StringComparison.Ordinal);

    public static string NormalizeObservation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var t = text.Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t;
    }
}
