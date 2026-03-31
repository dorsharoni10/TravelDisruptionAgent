using System.Text.RegularExpressions;

namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>
/// Extracts ordered airport codes for route search from free text (IATA tokens and common "from City to City" phrasing).
/// </summary>
public static partial class AirportRouteMessageParser
{
    private static readonly HashSet<string> NonAirportThreeLetterWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "THE", "AND", "FOR", "WAS", "ARE", "BUT", "NOT", "YOU", "ALL",
        "CAN", "HER", "ONE", "OUR", "OUT", "HAS", "HIS", "HOW", "ITS",
        "MAY", "NEW", "NOW", "OLD", "SEE", "WAY", "WHO", "DID", "GET",
        "HIM", "LET", "SAY", "SHE", "TOO", "USE", "ANY", "OWN"
    };

    private static readonly Dictionary<string, string> CityToIata =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["london"] = "LHR",
            ["paris"] = "CDG",
            ["chicago"] = "ORD",
            ["miami"] = "MIA",
            ["boston"] = "BOS",
            ["atlanta"] = "ATL",
            ["seattle"] = "SEA",
            ["denver"] = "DEN",
            ["dallas"] = "DFW",
            ["phoenix"] = "PHX",
            ["houston"] = "IAH",
            ["philadelphia"] = "PHL",
            ["detroit"] = "DTW",
            ["minneapolis"] = "MSP",
            ["charlotte"] = "CLT",
            ["las vegas"] = "LAS",
            ["orlando"] = "MCO",
            ["san francisco"] = "SFO",
            ["los angeles"] = "LAX",
            ["washington"] = "IAD",
            ["washington dc"] = "IAD",
            ["new york"] = "JFK",
            ["new york city"] = "JFK",
            ["nyc"] = "JFK",
            ["toronto"] = "YYZ",
            ["vancouver"] = "YVR",
            ["montreal"] = "YUL",
            ["tel aviv"] = "TLV",
            ["frankfurt"] = "FRA",
            ["amsterdam"] = "AMS",
            ["dubai"] = "DXB",
            ["tokyo"] = "NRT",
            ["rome"] = "FCO",
            ["barcelona"] = "BCN",
            ["madrid"] = "MAD",
            ["berlin"] = "BER",
            ["munich"] = "MUC",
            ["zurich"] = "ZRH",
            ["istanbul"] = "IST",
            ["singapore"] = "SIN",
            ["hong kong"] = "HKG",
            ["sydney"] = "SYD",
            ["melbourne"] = "MEL",
            ["athens"] = "ATH",
        };

    /// <summary>Returns distinct 3-letter airport codes in message order, preferring a parsed from→to pair when both resolve.</summary>
    public static List<string> ExtractLocationCodes(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var fromTo = TryParseFromToSegments(message);
        if (fromTo is { Origin: not null, Destination: not null })
            return [fromTo.Value.Origin, fromTo.Value.Destination];

        return ExtractBareIataCodes(message);
    }

    private static List<string> ExtractBareIataCodes(string message)
    {
        var upperMessage = message.ToUpperInvariant();
        return ThreeLetterCodeRegex().Matches(upperMessage)
            .Select(m => m.Value)
            .Where(c => !NonAirportThreeLetterWords.Contains(c))
            .Distinct()
            .ToList();
    }

    private static (string? Origin, string? Destination)? TryParseFromToSegments(string message)
    {
        var m = FromToRegex().Match(message);
        if (!m.Success)
            return null;

        var o = ResolveSegment(m.Groups[1].Value);
        var d = ResolveSegment(m.Groups[2].Value);
        if (o is null || d is null || string.Equals(o, d, StringComparison.OrdinalIgnoreCase))
            return null;

        return (o, d);
    }

    private static string? ResolveSegment(string raw)
    {
        var s = SanitizeEndpointSegment(raw);
        if (s.Length == 0)
            return null;

        if (s.Length == 3 && Regex.IsMatch(s, "^[A-Za-z]{3}$", RegexOptions.None))
        {
            var u = s.ToUpperInvariant();
            return NonAirportThreeLetterWords.Contains(u) ? null : u;
        }

        var key = Regex.Replace(s.ToLowerInvariant(), @"\s+", " ").Trim();
        if (CityToIata.TryGetValue(key, out var code))
            return code;

        return null;
    }

    /// <summary>Drop trailing time words so "ATH tomorrow" resolves to ATH.</summary>
    private static string SanitizeEndpointSegment(string raw)
    {
        var s = raw.Trim().TrimEnd('.', '?', '!', ',', ';', ':');
        var parts = Regex.Split(s.Trim(), @"\s+")
            .Where(p => p.Length > 0)
            .ToList();
        while (parts.Count > 0)
        {
            var last = parts[^1];
            if (last.Equals("tomorrow", StringComparison.OrdinalIgnoreCase) ||
                last.Equals("today", StringComparison.OrdinalIgnoreCase) ||
                last.Equals("tonight", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(last, @"^\d{1,4}[-/]\d", RegexOptions.None))
            {
                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            break;
        }

        return string.Join(' ', parts);
    }

    [GeneratedRegex(@"\bfrom\s+(.+?)\s+to\s+(.+?)(?=\s+was\b|\s+is\b|\s+been\b|\s+has\b|\s+got\b|\.|\?|\!|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromToRegex();

    [GeneratedRegex(@"\b[A-Z]{3}\b")]
    private static partial Regex ThreeLetterCodeRegex();
}
