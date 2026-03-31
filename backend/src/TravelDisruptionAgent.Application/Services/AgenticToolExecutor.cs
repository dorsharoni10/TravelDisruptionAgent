using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Utilities;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>Maps agent capability names to RAG retrieval and live travel tools.</summary>
public partial class AgenticToolExecutor : IAgenticToolExecutor
{
    private readonly IRagService _ragService;
    private readonly IToolExecutionCoordinator _tools;
    private readonly AgenticOptions _options;
    private readonly ILogger<AgenticToolExecutor> _logger;

    private static readonly HashSet<string> CommonNonAirportWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "THE", "AND", "FOR", "WAS", "ARE", "BUT", "NOT", "YOU", "ALL",
        "CAN", "HER", "ONE", "OUR", "OUT", "HAS", "HIS", "HOW", "ITS",
        "MAY", "NEW", "NOW", "OLD", "SEE", "WAY", "WHO", "DID", "GET",
        "HIM", "LET", "SAY", "SHE", "TOO", "USE"
    };

    public AgenticToolExecutor(
        IRagService ragService,
        IToolExecutionCoordinator tools,
        IOptions<AgenticOptions> options,
        ILogger<AgenticToolExecutor> logger)
    {
        _ragService = ragService;
        _tools = tools;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AgenticToolInvocationResult> InvokeAsync(
        string capability,
        IReadOnlyDictionary<string, string?> arguments,
        AgenticToolContext context,
        CancellationToken cancellationToken = default)
    {
        var cap = capability.Trim().ToLowerInvariant().Replace('-', '_');
        return cap switch
        {
            "search_policy_knowledge" => await SearchPolicyAsync(arguments, "search_policy_knowledge", internalIndex: false, cancellationToken),
            "search_internal_knowledge" => await SearchPolicyAsync(arguments, "search_internal_knowledge", internalIndex: true, cancellationToken),
            "search_flight_context" => await SearchFlightContextAsync(arguments, context, cancellationToken),
            "lookup_flight_by_number" => await LookupFlightNumberAsync(arguments, context, cancellationToken),
            "lookup_flight_by_route" => await LookupFlightRouteAsync(arguments, context, cancellationToken),
            "lookup_weather" => await LookupWeatherAsync(arguments, context, cancellationToken),
            _ => UnknownCapability(capability)
        };
    }

    private static AgenticToolInvocationResult UnknownCapability(string capability)
    {
        var obs = $"Unknown capability \"{capability}\". Valid: search_policy_knowledge, search_internal_knowledge, search_flight_context, lookup_flight_by_number, lookup_flight_by_route, lookup_weather.";
        return new AgenticToolInvocationResult
        {
            Observation = obs,
            ToolRecords =
            [
                new ToolExecutionResult(capability, "", obs, false, "Agent", 0, "unknown_capability")
            ]
        };
    }

    private async Task<AgenticToolInvocationResult> SearchPolicyAsync(
        IReadOnlyDictionary<string, string?> arguments,
        string toolName,
        bool internalIndex,
        CancellationToken ct)
    {
        var query = Arg(arguments, "query") ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            var msg = "Missing required argument \"query\" for policy search.";
            return new AgenticToolInvocationResult
            {
                Observation = msg,
                ToolRecords = [new ToolExecutionResult(toolName, "", msg, false, "Policy KB", 0, "missing_query")]
            };
        }

        var topK = ParseTopK(arguments, _options.DefaultPolicyTopK);
        var sw = Stopwatch.StartNew();
        var detail = await _ragService.RetrievePolicyKnowledgeDetailedAsync(query, topK, ct);
        sw.Stop();

        var norm = Application.Utilities.AgenticLoopHeuristics.NormalizePolicyQuery(query);
        var texts = detail.Chunks.Select(c => c.Content).ToList();
        var preview = BuildPolicyObservation(internalIndex, detail, query);
        var input = $"{(internalIndex ? "[internal] " : "")}{query} (top_k={topK})";

        _logger.LogInformation(
            "Agentic {Tool}: query=\"{Query}\", chunks={Chunks}, bestSim={BestSim}, candidatesAboveThreshold={Above}",
            toolName, query, detail.Chunks.Count, detail.BestSimilarity, detail.CandidatesAboveThreshold);

        return new AgenticToolInvocationResult
        {
            Observation = preview,
            ToolRecords =
            [
                new ToolExecutionResult(toolName, input, TruncateForToolRecord(preview), detail.Chunks.Count > 0,
                    "Policy Knowledge Base", sw.ElapsedMilliseconds,
                    detail.Chunks.Count == 0 ? "no_chunks_above_threshold" : null)
            ],
            WasPolicyRetrieval = true,
            NormalizedPolicyQuery = norm,
            PolicyChunkCount = detail.Chunks.Count,
            BestSimilarity = detail.BestSimilarity,
            PolicyChunkTexts = texts,
            PolicyRetrievalChunks = detail.Chunks
        };
    }

    private static string BuildPolicyObservation(bool internalIndex, PolicyRetrievalDetail detail, string query)
    {
        var sb = new StringBuilder();
        sb.Append(internalIndex
            ? "[Internal knowledge — same indexed corpus as policy; use for procedural / operational guidance]\n"
            : "[Company policy knowledge base]\n");
        sb.Append("Query: ").Append(query).Append('\n');
        if (detail.Chunks.Count == 0)
        {
            sb.Append("No matching policy passages above the relevance threshold. Do not invent policy; state that evidence was not found.");
            return sb.ToString();
        }

        sb.Append($"Retrieved {detail.Chunks.Count} passage(s). Best similarity: {detail.BestSimilarity:F3}.\n---\n");
        foreach (var c in detail.Chunks)
        {
            sb.Append('[').Append(c.DocumentId).Append("] score=").Append(c.SimilarityScore?.ToString("F3") ?? "n/a")
                .Append('\n').Append(Truncate(c.Content, 1200)).Append("\n---\n");
        }

        return sb.ToString();
    }

    private async Task<AgenticToolInvocationResult> LookupFlightNumberAsync(
        IReadOnlyDictionary<string, string?> arguments,
        AgenticToolContext context,
        CancellationToken ct)
    {
        var argFn = Arg(arguments, "flight_number") ?? Arg(arguments, "flight");
        if (!TryResolveGroundedFlightNumber(argFn, context.UserMessage, out var fn, out var groundedRejectReason))
        {
            var msg = groundedRejectReason
                      ?? "No flight number in arguments or user message.";
            return new AgenticToolInvocationResult
            {
                Observation = msg,
                ToolRecords =
                [
                    new ToolExecutionResult("lookup_flight_by_number", "", msg, false, "Agent", 0,
                        groundedRejectReason is null ? "no_flight_number" : "flight_number_not_in_user_message")
                ]
            };
        }
        var date = ParseDateArg(arguments, context.RequestTimeUtc)
                   ?? ExtractDate(context.UserMessage)
                   ?? context.RequestTimeUtc.Date;

        var sw = Stopwatch.StartNew();
        var r = await _tools.LookupFlightByNumberAsync(fn, date, ct);
        sw.Stop();
        return new AgenticToolInvocationResult
        {
            Observation = FormatToolObservation("Flight by number", r),
            ToolRecords = [r]
        };
    }

    private async Task<AgenticToolInvocationResult> LookupFlightRouteAsync(
        IReadOnlyDictionary<string, string?> arguments,
        AgenticToolContext context,
        CancellationToken ct)
    {
        var origin = Arg(arguments, "origin") ?? Arg(arguments, "from");
        var dest = Arg(arguments, "destination") ?? Arg(arguments, "to");
        if ((string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(dest)))
        {
            var locs = ExtractLocations(context.UserMessage);
            if (locs.Count >= 2)
            {
                if (string.IsNullOrWhiteSpace(origin)) origin = locs[0];
                if (string.IsNullOrWhiteSpace(dest)) dest = locs[1];
            }
        }

        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(dest))
        {
            const string msg = "Route lookup needs origin and destination (IATA codes) in arguments or user message.";
            return new AgenticToolInvocationResult
            {
                Observation = msg,
                ToolRecords = [new ToolExecutionResult("lookup_flight_by_route", "", msg, false, "Agent", 0, "no_route")]
            };
        }

        if (!UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(context.UserMessage))
        {
            const string msg =
                "Route / alternative flight search was not run because the user did not explicitly ask for other flights, rebooking, or a route search. Use lookup_flight_by_number for a specific flight.";
            return new AgenticToolInvocationResult
            {
                Observation = msg,
                ToolRecords = [new ToolExecutionResult("lookup_flight_by_route", "", msg, false, "Agent", 0, "skipped_not_requested")]
            };
        }

        var date = ParseDateArg(arguments, context.RequestTimeUtc)
                   ?? ExtractDate(context.UserMessage)
                   ?? context.RequestTimeUtc.Date;

        var input = $"{origin} → {dest} on {date:yyyy-MM-dd}";
        var sw = Stopwatch.StartNew();
        var r = await _tools.LookupFlightByRouteAsync(origin.ToUpperInvariant(), dest.ToUpperInvariant(), date, ct);
        sw.Stop();
        return new AgenticToolInvocationResult
        {
            Observation = FormatToolObservation("Flight by route", r),
            ToolRecords = [r]
        };
    }

    private async Task<AgenticToolInvocationResult> LookupWeatherAsync(
        IReadOnlyDictionary<string, string?> arguments,
        AgenticToolContext context,
        CancellationToken ct)
    {
        var loc = Arg(arguments, "location") ?? Arg(arguments, "airport_code");
        if (string.IsNullOrWhiteSpace(loc))
        {
            var locs = ExtractLocations(context.UserMessage);
            loc = locs.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(loc))
        {
            const string msg = "Weather lookup needs \"location\" (city or IATA) in arguments or user message.";
            return new AgenticToolInvocationResult
            {
                Observation = msg,
                ToolRecords = [new ToolExecutionResult("lookup_weather", "", msg, false, "Agent", 0, "no_location")]
            };
        }

        if (!UserToolRequestSignals.ExplicitlyAsksForWeather(context.UserMessage))
        {
            const string msg =
                "Weather lookup was not run because the user did not explicitly ask for weather or forecast information.";
            return new AgenticToolInvocationResult
            {
                Observation = msg,
                ToolRecords = [new ToolExecutionResult("lookup_weather", loc, msg, false, "Agent", 0, "skipped_not_requested")]
            };
        }

        var sw = Stopwatch.StartNew();
        var r = await _tools.LookupWeatherAsync(loc.Trim(), ct);
        sw.Stop();
        return new AgenticToolInvocationResult
        {
            Observation = FormatToolObservation("Weather", r),
            ToolRecords = [r]
        };
    }

    private async Task<AgenticToolInvocationResult> SearchFlightContextAsync(
        IReadOnlyDictionary<string, string?> arguments,
        AgenticToolContext context,
        CancellationToken ct)
    {
        var records = new List<ToolExecutionResult>();
        var obs = new StringBuilder();
        obs.AppendLine("[Flight / route / weather context]");

        var msg = context.UserMessage;
        var date = ParseDateArg(arguments, context.RequestTimeUtc)
                   ?? ExtractDate(msg)
                   ?? context.RequestTimeUtc.Date;

        var explicitFn = Arg(arguments, "flight_number") ?? Arg(arguments, "flight");
        var numbers = new List<string>();
        var fromMsgNumbers = ExtractFlightNumbers(msg);
        var argNorm = NormalizeFlightNumberArg(explicitFn);
        if (fromMsgNumbers.Count > 0)
        {
            if (!string.IsNullOrEmpty(argNorm) &&
                fromMsgNumbers.Contains(argNorm, StringComparer.OrdinalIgnoreCase))
                numbers.Add(argNorm);
            else
                numbers.AddRange(fromMsgNumbers);
        }
        // Do not trust model-supplied flight_number when the user message contains none (avoids hallucinated lookups).

        foreach (var fn in numbers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sw = Stopwatch.StartNew();
            var r = await _tools.LookupFlightByNumberAsync(fn, date, ct);
            sw.Stop();
            records.Add(r);
            obs.AppendLine(FormatToolObservation($"Flight {fn}", r));
        }

        var origin = Arg(arguments, "origin") ?? Arg(arguments, "from");
        var dest = Arg(arguments, "destination") ?? Arg(arguments, "to");
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(dest))
        {
            var locs = ExtractLocations(msg);
            if (locs.Count >= 2)
            {
                if (string.IsNullOrWhiteSpace(origin)) origin = locs[0];
                if (string.IsNullOrWhiteSpace(dest)) dest = locs[1];
            }
        }

        if (!string.IsNullOrWhiteSpace(origin) && !string.IsNullOrWhiteSpace(dest) &&
            UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(msg))
        {
            var sw = Stopwatch.StartNew();
            var r = await _tools.LookupFlightByRouteAsync(
                origin.ToUpperInvariant(), dest.ToUpperInvariant(), date, ct);
            sw.Stop();
            records.Add(r);
            obs.AppendLine(FormatToolObservation("Route search", r));
        }

        var wantsWeather = UserToolRequestSignals.ExplicitlyAsksForWeather(msg);
        var weatherExtra = Arg(arguments, "weather_locations");
        var weatherLocs = new List<string>();
        if (wantsWeather && !string.IsNullOrWhiteSpace(weatherExtra))
            weatherLocs.AddRange(weatherExtra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var iataFromMsg = ExtractLocations(msg);
        if (wantsWeather)
        {
            foreach (var c in iataFromMsg.Take(2))
            {
                if (!weatherLocs.Contains(c, StringComparer.OrdinalIgnoreCase))
                    weatherLocs.Add(c);
            }
        }

        if (wantsWeather && records.Count > 0 && weatherLocs.Count == 0)
        {
            var flightOut = records.FirstOrDefault(r => r.ToolName.Contains("Number", StringComparison.Ordinal) && r.Success);
            if (flightOut is not null)
            {
                var matches = ThreeLetterCodeRegex().Matches(flightOut.Output);
                foreach (Match m in matches)
                {
                    var code = m.Value;
                    if (!CommonNonAirportWords.Contains(code) &&
                        !weatherLocs.Contains(code, StringComparer.OrdinalIgnoreCase))
                        weatherLocs.Add(code);
                    if (weatherLocs.Count >= 2) break;
                }
            }
        }

        foreach (var loc in weatherLocs.Take(3).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sw = Stopwatch.StartNew();
            var r = await _tools.LookupWeatherAsync(loc, ct);
            sw.Stop();
            records.Add(r);
            obs.AppendLine(FormatToolObservation($"Weather ({loc})", r));
        }

        if (records.Count == 0)
        {
            obs.AppendLine("No flight numbers, route pair, or weather locations could be resolved. Use explicit arguments or more specific lookup_* tools.");
            records.Add(new ToolExecutionResult("search_flight_context", msg, obs.ToString(), false, "Agent", 0, "no_context"));
        }

        return new AgenticToolInvocationResult
        {
            Observation = obs.ToString(),
            ToolRecords = records
        };
    }

    private static string FormatToolObservation(string label, ToolExecutionResult r)
    {
        var status = r.Success ? "OK" : "FAILED";
        return $"[{label}] {status} ({r.ToolName})\n{Truncate(r.Output, 2500)}";
    }

    private static string? Arg(IReadOnlyDictionary<string, string?> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.Trim() : null;

    private static int ParseTopK(IReadOnlyDictionary<string, string?> d, int fallback)
    {
        var raw = Arg(d, "top_k") ?? Arg(d, "topK");
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k) && k is > 0 and < 20
            ? k
            : fallback;
    }

    private static DateTime? ParseDateArg(IReadOnlyDictionary<string, string?> d, DateTime requestUtc)
    {
        var raw = Arg(d, "date") ?? Arg(d, "flight_date");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt.Date;
        return null;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s[..max] + "\n…(truncated)";
    }

    private static string TruncateForToolRecord(string s) => Truncate(s, 8000);

    private static string? NormalizeFlightNumberArg(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);

    /// <summary>
    /// Resolves a flight number only when grounded in the user message (extracted via pattern).
    /// Model-supplied numbers without a match in user text are rejected to avoid hallucinated lookups.
    /// </summary>
    private static bool TryResolveGroundedFlightNumber(
        string? argumentFlightNumber,
        string userMessage,
        out string flightNumber,
        out string? rejectionReason)
    {
        flightNumber = "";
        rejectionReason = null;
        var fromMsg = ExtractFlightNumbers(userMessage);
        var argNorm = NormalizeFlightNumberArg(argumentFlightNumber);

        if (fromMsg.Count > 0)
        {
            flightNumber = !string.IsNullOrEmpty(argNorm) &&
                           fromMsg.Contains(argNorm, StringComparer.OrdinalIgnoreCase)
                ? argNorm
                : fromMsg[0];
            return true;
        }

        if (!string.IsNullOrEmpty(argNorm))
        {
            rejectionReason =
                "Flight number from tool arguments was ignored because it does not appear in the user message (do not invent flight numbers). Use lookup_flight_by_route with origin and destination from the user, search_flight_context, or ask for the flight number.";
            return false;
        }

        return false;
    }

    private static List<string> ExtractFlightNumbers(string message) =>
        FlightNumberRegex().Matches(message)
            .Select(m => m.Value.ToUpperInvariant().Replace(" ", ""))
            .Distinct()
            .ToList();

    private static List<string> ExtractLocations(string message) =>
        AirportRouteMessageParser.ExtractLocationCodes(message);

    private static DateTime? ExtractDate(string message)
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

    [GeneratedRegex(@"\b([A-Z]{2})\s?(\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlightNumberRegex();

    [GeneratedRegex(@"\b[A-Z]{3}\b")]
    private static partial Regex ThreeLetterCodeRegex();

    [GeneratedRegex(@"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b|\b\d{1,2}[-/]\d{1,2}[-/]\d{4}\b")]
    private static partial Regex DateRegex();
}
