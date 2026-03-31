using System.Globalization;
using System.Text.RegularExpressions;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Utilities;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>Shared parsing helpers for flight/route extraction used by the agent orchestration pipeline.</summary>
public static partial class OrchestrationParseHelpers
{
    public static readonly HashSet<string> CommonNonAirportWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "THE", "AND", "FOR", "WAS", "ARE", "BUT", "NOT", "YOU", "ALL",
        "CAN", "HER", "ONE", "OUR", "OUT", "HAS", "HIS", "HOW", "ITS",
        "MAY", "NEW", "NOW", "OLD", "SEE", "WAY", "WHO", "DID", "GET",
        "HIM", "LET", "SAY", "SHE", "TOO", "USE"
    };

    public static List<string> ExtractFlightNumbers(string message) =>
        FlightNumberRegex().Matches(message)
            .Select(m => m.Value.ToUpperInvariant().Replace(" ", ""))
            .Distinct()
            .ToList();

    public static List<string> ExtractLocations(string message) =>
        AirportRouteMessageParser.ExtractLocationCodes(message);

    public static DateTime? ExtractDate(string message)
    {
        var dateMatch = DateRegex().Match(message);
        if (dateMatch.Success && DateTime.TryParse(dateMatch.Value, out var parsed))
            return parsed;
        if (message.Contains("today", StringComparison.OrdinalIgnoreCase))
            return DateTime.UtcNow.Date;
        if (message.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
            return DateTime.UtcNow.Date.AddDays(1);
        return null;
    }

    public static DateTime? TryExtractDepartureCalendarDateFromPrimaryFlightTool(
        IReadOnlyList<ToolExecutionResult> toolResults)
    {
        foreach (var t in toolResults.Where(x => x.ToolName == "FlightLookupByNumber" && x.Success))
        {
            var m = Regex.Match(t.Output, @"Scheduled departure:\s*(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase);
            if (m.Success &&
                DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sched))
                return DateTime.SpecifyKind(sched.Date, DateTimeKind.Utc);

            m = Regex.Match(t.Output, @"Actual departure:\s*(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase);
            if (m.Success &&
                DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var act))
                return DateTime.SpecifyKind(act.Date, DateTimeKind.Utc);
        }

        return null;
    }

    public static List<string> ExtractWeatherLocationNames(string message)
    {
        var normalized = Regex.Replace(message, @"[^\w\s'\-]", " ");
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];

        var prepositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "in", "at", "near", "around", "for"
        };

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "today", "tomorrow", "now", "currently", "right", "please", "and", "or",
            "with", "on", "is", "are", "was", "were", "will", "should", "can", "could",
            "would", "do", "does", "did", "my", "your", "the", "a", "an", "weather",
            "flight", "flights", "airport", "delay", "delayed", "cancelled", "canceled",
            "help", "what", "how", "when", "where", "why", "which", "me"
        };

        var results = new List<string>();

        for (var i = 0; i < words.Length; i++)
        {
            if (!prepositions.Contains(words[i])) continue;

            var parts = new List<string>();
            for (var j = i + 1; j < words.Length && parts.Count < 4; j++)
            {
                var token = words[j].Trim('\'', '-', '"');
                if (string.IsNullOrWhiteSpace(token)) break;
                if (stopWords.Contains(token)) break;

                if (token.All(char.IsDigit)) break;

                parts.Add(token);
            }

            if (parts.Count == 0) continue;

            var candidate = string.Join(" ", parts);
            if (candidate.Length < 2) continue;

            results.Add(candidate);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [GeneratedRegex(@"\b([A-Z]{2})\s?(\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlightNumberRegex();

    [GeneratedRegex(@"\b[A-Z]{3}\b")]
    public static partial Regex ThreeLetterCodeRegex();

    [GeneratedRegex(@"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b|\b\d{1,2}[-/]\d{1,2}[-/]\d{4}\b")]
    private static partial Regex DateRegex();
}
