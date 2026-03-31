using System.Globalization;
using System.Text.RegularExpressions;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Utilities;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>
/// Builds a <see cref="VerifiedContext"/> from raw tool results, RAG context,
/// and plan validation — extracting only verified, structured facts.
/// </summary>
public static partial class VerifiedContextBuilder
{
    /// <summary>Default 24h forward from request, then +1 day steps up to 5 days (120h), same 1h minimum departure lead time.</summary>
    private static readonly int[] AlternativeSearchHorizonTiersHours = [24, 48, 72, 96, 120];

    public static VerifiedContext Build(
        string intent,
        List<ToolExecutionResult> toolResults,
        IReadOnlyList<string>? ragContext,
        PlanValidationResult? validation,
        DateTime? requestTime = null)
    {
        var executedTools = toolResults.Select(t => new ExecutedToolSummary(
            t.ToolName, t.Input, t.Success, t.DataSource, t.ErrorMessage)).ToList();

        var skippedSteps = (validation?.MissingTools ?? [])
            .Select(tool => new SkippedStep(
                $"{tool} was planned but not executed",
                validation!.Issues.FirstOrDefault(i => i.ToolName == tool)?.Description ?? "Unknown"))
            .ToList();

        var flights = NormalizeFlights(toolResults);
        var weather = NormalizeWeather(toolResults);

        var primaryFlightNumbers = ExtractPrimaryFlightNumbers(toolResults);

        var effectiveRequestTime = requestTime ?? DateTime.UtcNow;
        var (cleanAlternatives, horizonHours, foundIn24h, windowNote, _) =
            BuildCleanAlternativesProgressive(flights, primaryFlightNumbers, effectiveRequestTime);

        cleanAlternatives = MergeHistoryFallbackAlternatives(
            cleanAlternatives, flights, primaryFlightNumbers);

        var primaryFlights = flights.Where(f => f.IsPrimaryLookup).ToList();

        var verifiedFacts = new List<string>();
        var unverifiedFacts = new List<string>();

        ClassifyFlightFacts(primaryFlights, cleanAlternatives, intent, verifiedFacts, unverifiedFacts);
        AddRouteDataVersusFilterUnverified(toolResults, cleanAlternatives.Count, unverifiedFacts);
        ClassifyWeatherFacts(weather, intent, verifiedFacts, unverifiedFacts);

        if (validation is not null)
        {
            foreach (var issue in validation.Issues)
            {
                if (issue.Type == PlanIssueType.Contradiction)
                    unverifiedFacts.Add(issue.Description);
            }
        }

        var policies = ragContext?.ToList() ?? [];

        return new VerifiedContext
        {
            Intent = intent,
            ExecutedTools = executedTools,
            SkippedSteps = skippedSteps,
            Flights = flights,
            Weather = weather,
            VerifiedFacts = verifiedFacts,
            UnverifiedFacts = unverifiedFacts,
            PolicyContext = policies,
            Contradictions = validation?.Contradictions ?? [],
            CleanAlternatives = cleanAlternatives,
            AlternativeSearchHorizonHours = horizonHours,
            AlternativesFoundInDefault24hWindow = foundIn24h,
            AlternativeWindowContextNote = windowNote
        };
    }

    // ── Flight normalization ──────────────────────────────────────────

    private static List<NormalizedFlightData> NormalizeFlights(List<ToolExecutionResult> toolResults)
    {
        var results = new List<NormalizedFlightData>();

        foreach (var tool in toolResults.Where(t => t.ToolName.Contains("Flight") && t.Success))
        {
            bool isFallback = tool.DataSource.Contains("fallback", StringComparison.OrdinalIgnoreCase);
            bool isPrimary = tool.ToolName == "FlightLookupByNumber";

            if (tool.ToolName == "FlightLookupByRoute")
            {
                results.AddRange(NormalizeRouteOutputSegments(tool.Output, tool.DataSource, isFallback));
            }
            else
            {
                results.Add(ParseSingleFlight(tool.Output, tool.DataSource, isFallback, isPrimary));
            }
        }

        return results;
    }

    /// <summary>Parses only route-search segments (never primary lookup).</summary>
    public static List<NormalizedFlightData> NormalizeRouteOutputSegments(
        string output, string dataSource, bool isFallback)
    {
        var results = new List<NormalizedFlightData>();
        foreach (var segment in output.Split("---", StringSplitOptions.RemoveEmptyEntries))
            results.Add(ParseSingleFlight(segment.Trim(), dataSource, isFallback, isPrimary: false));
        return results;
    }

    public static HashSet<string> ExtractPrimaryFlightNumbers(IReadOnlyList<ToolExecutionResult> toolResults)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in toolResults.Where(x => x.ToolName == "FlightLookupByNumber" && x.Success))
        {
            if (!string.IsNullOrWhiteSpace(t.Input))
                set.Add(t.Input.Trim().Replace(" ", ""));
            foreach (Match m in FlightNumberInOutputRegex().Matches(t.Output))
            {
                if (m.Groups.Count > 1 && m.Groups[1].Success)
                    set.Add(m.Groups[1].Value.Replace(" ", ""));
            }
        }

        return set;
    }

    /// <summary>
    /// Human-readable route result for UI / SSE: same filtering as <see cref="VerifiedContext.CleanAlternatives"/>.
    /// </summary>
    public static string BuildFilteredRouteDisplayText(
        string rawRouteOutput,
        IReadOnlyList<ToolExecutionResult> toolResultsSoFar,
        DateTime requestUtc,
        string dataSource,
        bool isFallback) =>
        BuildFilteredRouteDisplay(rawRouteOutput, toolResultsSoFar, requestUtc, dataSource, isFallback).DisplayText;

    /// <inheritdoc cref="BuildFilteredRouteDisplayText"/>
    public static (string DisplayText, RouteAlternativePipelineDiagnostics Diagnostics) BuildFilteredRouteDisplay(
        string rawRouteOutput,
        IReadOnlyList<ToolExecutionResult> toolResultsSoFar,
        DateTime requestUtc,
        string dataSource,
        bool isFallback)
    {
        var primaryNumbers = ExtractPrimaryFlightNumbers(toolResultsSoFar);
        var routeFlights = NormalizeRouteOutputSegments(rawRouteOutput, dataSource, isFallback);
        var (clean, horizonHours, foundIn24h, windowNote, diagBase) =
            BuildCleanAlternativesProgressive(routeFlights, primaryNumbers, requestUtc);
        var diag = diagBase with { NormalizedInputRows = routeFlights.Count };
        var display = FormatRouteAlternativesForDisplay(
            clean,
            requestUtc,
            horizonHours,
            foundIn24h,
            windowNote,
            rawSegmentCount: routeFlights.Count,
            routeSearchSucceeded: true,
            providerDeclaredFlightCount: TryParseProviderFlightCountFromOutput(rawRouteOutput));
        return (display, diag);
    }

    /// <summary>Parses "Found N flight(s)" from coordinator route tool output.</summary>
    public static int TryParseProviderFlightCountFromOutput(string output)
    {
        var m = Regex.Match(output, @"Found\s+(\d+)\s+flight", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    /// <summary>Replace raw FlightLookupByRoute outputs in a copy for API clients.</summary>
    public static List<ToolExecutionResult> ToDisplayToolResults(
        IReadOnlyList<ToolExecutionResult> raw,
        VerifiedContext ctx,
        DateTime requestUtc)
    {
        var list = raw.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t.ToolName != "FlightLookupByRoute" || !t.Success) continue;
            list[i] = t with
            {
                Output = FormatRouteAlternativesForDisplay(
                    ctx.CleanAlternatives,
                    requestUtc,
                    ctx.AlternativeSearchHorizonHours,
                    ctx.AlternativesFoundInDefault24hWindow,
                    ctx.AlternativeWindowContextNote,
                    rawSegmentCount: null,
                    routeSearchSucceeded: true,
                    providerDeclaredFlightCount: null)
            };
        }

        return list;
    }

    private static string FormatRouteAlternativesForDisplay(
        IReadOnlyList<NormalizedFlightData> cleanAlternatives,
        DateTime requestUtc,
        int horizonHours,
        bool foundInDefault24h,
        string? windowContextNote,
        int? rawSegmentCount,
        bool routeSearchSucceeded,
        int? providerDeclaredFlightCount)
    {
        var earliest = requestUtc.AddHours(1);
        var latest = requestUtc.AddHours(horizonHours);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "Filtered view (matches final recommendation): departures from " +
            $"{earliest:yyyy-MM-dd HH:mm} UTC through {latest:yyyy-MM-dd HH:mm} UTC " +
            $"(minimum 1 hour after your request; search horizon up to {horizonHours} hours / {horizonHours / 24} days when widened).");
        if (!foundInDefault24h && cleanAlternatives.Count > 0 && windowContextNote is not null)
        {
            sb.AppendLine(windowContextNote);
        }

        if (rawSegmentCount is > 0)
            sb.AppendLine("The provider may return additional flights outside this window; they are hidden here.");
        sb.AppendLine();

        if (cleanAlternatives.Count == 0)
        {
            if (routeSearchSucceeded)
            {
                var hadProviderRows = (rawSegmentCount ?? 0) > 0 ||
                    (providerDeclaredFlightCount ?? 0) > 0;
                if (hadProviderRows)
                {
                    sb.AppendLine(
                        "The live data provider returned flight record(s) for this route, but none have a scheduled departure " +
                        "that falls in the forward rebooking window above (UTC), after excluding your own flight number and applying quality filters. " +
                        "Aviation feeds often emphasize active or same-day movements rather than a full future schedule.");
                    sb.Append(
                        "Do not tell the passenger that no alternative flights exist — only that the live feed could not confirm " +
                        "bookable alternatives in this window; suggest checking the airline website or a booking site.");
                    if (windowContextNote is not null)
                    {
                        sb.AppendLine();
                        sb.Append(windowContextNote);
                    }
                }
                else
                {
                    sb.Append(windowContextNote ?? (
                        "No alternative flights were found from 1 hour after your request through 120 hours (5 days) forward " +
                        "(search widened in 24-hour steps: 24h → 48h → 72h → 96h → 120h)."));
                }
            }
            else
                sb.Append("Route search did not return usable flight data.");
            return sb.ToString();
        }

        sb.AppendLine($"Found {cleanAlternatives.Count} alternative flight(s) in this window:");
        sb.AppendLine();
        foreach (var f in cleanAlternatives)
        {
            sb.AppendLine($"Flight {f.FlightNumber} ({f.Airline}): {InferEffectiveStatus(f)}");
            if (!string.IsNullOrEmpty(f.Route))
                sb.AppendLine($"Route: {f.Route}");
            if (f.ScheduledDeparture is not null)
                sb.AppendLine($"Scheduled departure: {f.ScheduledDeparture}");
            if (f.ScheduledArrival is not null)
                sb.AppendLine($"Scheduled arrival: {f.ScheduledArrival}");
            if (f.ActualDeparture is not null)
                sb.AppendLine($"Actual departure: {f.ActualDeparture}");
            if (f.ActualArrival is not null)
                sb.AppendLine($"Actual arrival: {f.ActualArrival}");
            if (f.Gate is not null)
                sb.AppendLine($"Gate: {f.Gate}");
            if (f.CodesharePartners.Count > 0)
                sb.AppendLine($"Also marketed as: {string.Join(", ", f.CodesharePartners)}");
            sb.AppendLine("---");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Progressive windows: request+1h through request+24h, then 48h, 72h, 96h, 120h until matches or exhausted.
    /// Excludes primary flight, codeshare-dedupes, airline name sanity.
    /// </summary>
    private static (List<NormalizedFlightData> alternatives, int horizonHoursUsed, bool foundInDefault24h, string? contextNote, RouteAlternativePipelineDiagnostics diagnostics)
        BuildCleanAlternativesProgressive(
            IReadOnlyList<NormalizedFlightData> allFlights,
            HashSet<string> primaryFlightNumbers,
            DateTime requestTime)
    {
        var normalizedInputRows = allFlights.Count;
        var excludedPrimary = allFlights.Count(f => primaryFlightNumbers.Contains(f.FlightNumber));
        var earliest = requestTime.AddHours(1);
        var pool = allFlights
            .Where(f => !f.IsPrimaryLookup)
            .Where(f => !primaryFlightNumbers.Contains(f.FlightNumber))
            .Where(f => f.DataQuality != FlightDataQuality.Unavailable)
            .Where(f => f.Airline.Length > 1)
            .ToList();

        var tierSnapshots = new List<RouteAlternativeTierDiagnostics>();
        var sampleTimes = pool
            .Select(f => ParseDepartureInstantUtcForWindow(f))
            .Where(d => d.HasValue)
            .Take(5)
            .Select(d => d!.Value.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture));

        foreach (var maxHours in AlternativeSearchHorizonTiersHours)
        {
            var latest = requestTime.AddHours(maxHours);
            var slice = pool
                .Where(f => DepartsInWindow(f, earliest, latest))
                .ToList();
            var deduped = DeduplicateCodeshares(slice);
            tierSnapshots.Add(new RouteAlternativeTierDiagnostics(maxHours, slice.Count, deduped.Count));
            var ordered = deduped
                .OrderBy(f => ParseDepartureInstantUtcForWindow(f) ?? DateTime.MaxValue)
                .ToList();
            if (ordered.Count == 0)
                continue;

            var foundIn24 = maxHours == 24;
            string? note = foundIn24
                ? null
                : "No alternatives were found in the next 24 hours (departing at least 1 hour after your request). " +
                  $"Search was expanded to the next {maxHours} hours ({maxHours / 24} days); the flights below are within that wider window.";
            var diag = new RouteAlternativePipelineDiagnostics(
                normalizedInputRows,
                excludedPrimary,
                pool.Count,
                tierSnapshots,
                ordered.Count,
                string.Join(", ", sampleTimes));
            return (ordered, maxHours, foundIn24, note, diag);
        }

        const int maxTried = 120;
        var emptyNote =
            "No alternatives were found in the next 24 hours (departing at least 1 hour after your request). " +
            "Search was expanded stepwise to 48h, then 72h, then 96h, then 120h (5 days) — still no qualifying flights in provider data.";
        var emptyDiag = new RouteAlternativePipelineDiagnostics(
            normalizedInputRows,
            excludedPrimary,
            pool.Count,
            tierSnapshots,
            0,
            string.Join(", ", sampleTimes));
        return ([], maxTried, false, emptyNote, emptyDiag);
    }

    /// <summary>
    /// Route rows from <see cref="ConversationHistoryRouteFallback"/> skip forward-departure UTC windows;
    /// still surface them as alternatives for follow-up Q&amp;A and guardrails.
    /// </summary>
    private static List<NormalizedFlightData> MergeHistoryFallbackAlternatives(
        List<NormalizedFlightData> clean,
        IReadOnlyList<NormalizedFlightData> allFlights,
        HashSet<string> primaryFlightNumbers)
    {
        var history = allFlights
            .Where(f => f.DataSource.Contains(ConversationHistoryRouteFallback.SyntheticDataSource, StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.IsPrimaryLookup)
            .Where(f => !primaryFlightNumbers.Contains(f.FlightNumber))
            .Where(f => f.DataQuality != FlightDataQuality.Unavailable)
            .ToList();
        if (history.Count == 0)
            return clean;

        var merged = new List<NormalizedFlightData>(clean);
        foreach (var h in history)
        {
            if (merged.Any(m =>
                    m.ScheduledDeparture == h.ScheduledDeparture &&
                    m.ScheduledArrival == h.ScheduledArrival &&
                    string.Equals(m.Route, h.Route, StringComparison.Ordinal)))
                continue;
            merged.Insert(0, h);
        }

        return merged;
    }

    private static void AddRouteDataVersusFilterUnverified(
        IReadOnlyList<ToolExecutionResult> toolResults,
        int cleanAlternativeCount,
        List<string> unverifiedFacts)
    {
        if (cleanAlternativeCount > 0) return;
        foreach (var t in toolResults.Where(x => x.ToolName == "FlightLookupByRoute" && x.Success))
        {
            var declared = TryParseProviderFlightCountFromOutput(t.Output);
            var isFb = t.DataSource.Contains("fallback", StringComparison.OrdinalIgnoreCase);
            var parsed = NormalizeRouteOutputSegments(t.Output, t.DataSource, isFb).Count;
            if (declared > 0 || parsed > 0)
            {
                unverifiedFacts.Add(
                    "Live route data returned flight record(s), but none passed the forward-departure window (UTC) used for rebooking. " +
                    "Do not state that no alternative flights exist; suggest the airline site or a booking engine.");
                return;
            }
        }
    }

    /// <summary>
    /// True if scheduled departure (preferred for rebooking) falls between earliest and latest (UTC).
    /// Preferring scheduled over actual avoids dropping future itineraries when the feed marks an earlier actual time.
    /// </summary>
    private static bool DepartsInWindow(NormalizedFlightData f, DateTime earliest, DateTime latest)
    {
        var depTime = ParseDepartureInstantUtcForWindow(f);
        if (depTime is null) return false;
        return depTime.Value >= earliest && depTime.Value <= latest;
    }

    /// <summary>Scheduled departure first (rebooking), then actual; parsed as UTC when the string has no offset.</summary>
    private static DateTime? ParseDepartureInstantUtcForWindow(NormalizedFlightData f)
    {
        if (TryParseUtcPreferred(f.ScheduledDeparture, out var sched))
            return sched;
        if (TryParseUtcPreferred(f.ActualDeparture, out var act))
            return act;
        return null;
    }

    private static bool TryParseUtcPreferred(string? s, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (DateTime.TryParse(
                s.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc))
            return true;
        return false;
    }

    /// <summary>
    /// Flights sharing the same departure time AND gate are codeshares of the same physical aircraft.
    /// Keep the one with the longest/most-recognizable airline name and attach codeshare partners.
    /// </summary>
    private static List<NormalizedFlightData> DeduplicateCodeshares(List<NormalizedFlightData> flights)
    {
        var groups = flights
            .GroupBy(f => new
            {
                DepTime = f.ScheduledDeparture ?? f.ActualDeparture ?? "",
                Gate = f.Gate ?? "",
                Route = f.Route
            })
            .ToList();

        var result = new List<NormalizedFlightData>();
        foreach (var group in groups)
        {
            if (group.Key.DepTime == "" && group.Key.Gate == "")
            {
                result.AddRange(group);
                continue;
            }

            var ordered = group.OrderByDescending(f => f.Airline.Length).ToList();
            var primary = ordered[0];
            var partners = ordered.Skip(1).Select(f => $"{f.FlightNumber} ({f.Airline})").ToList();
            result.Add(primary with { CodesharePartners = partners });
        }

        return result;
    }

    private static NormalizedFlightData ParseSingleFlight(string text, string dataSource, bool isFallback, bool isPrimary = false)
    {
        var flightNumber = ExtractField(text, @"Flight\s+([A-Z]{2}\s?\d{1,4})")?.Replace(" ", "") ?? "Unknown";
        var airline = ExtractField(text, @"\(([^)]+)\)") ?? "";
        if (airline.Length <= 1) airline = "";
        var status = ExtractField(text, @":\s*(.+)", firstLineOnly: true) ?? "";

        var depAirport = "";
        var arrAirport = "";
        var routeMatch = RouteRegex().Match(text);
        if (routeMatch.Success)
        {
            depAirport = routeMatch.Groups[1].Value.Trim();
            arrAirport = routeMatch.Groups[2].Value.Trim();
        }

        var scheduledDep = ExtractField(text, @"Scheduled departure:\s*(.+)");
        var scheduledArr = ExtractField(text, @"Scheduled arrival:\s*(.+)");
        var actualDep = ExtractField(text, @"Actual departure:\s*(.+)");
        var actualArr = ExtractField(text, @"Actual arrival:\s*(.+)");
        var delay = ExtractField(text, @"Delay:\s*(\d+)\s*minutes?");
        var gate = ExtractField(text, @"Gate:\s*(.+)");

        var quality = (actualDep ?? actualArr) is not null
            ? FlightDataQuality.Full
            : (scheduledDep ?? scheduledArr) is not null
                ? FlightDataQuality.ScheduledOnly
                : FlightDataQuality.Unavailable;

        return new NormalizedFlightData
        {
            FlightNumber = flightNumber,
            Airline = airline,
            Status = status.Trim(),
            Route = $"{depAirport} → {arrAirport}",
            DepartureAirport = depAirport,
            ArrivalAirport = arrAirport,
            ScheduledDeparture = scheduledDep,
            ScheduledArrival = scheduledArr,
            ActualDeparture = actualDep,
            ActualArrival = actualArr,
            DelayMinutes = delay,
            Gate = gate,
            DataQuality = quality,
            DataSource = dataSource,
            IsFromFallback = isFallback,
            IsPrimaryLookup = isPrimary
        };
    }

    // ── Weather normalization ─────────────────────────────────────────

    private static List<NormalizedWeatherData> NormalizeWeather(List<ToolExecutionResult> toolResults)
    {
        return toolResults
            .Where(t => t.ToolName == "WeatherLookup" && t.Success)
            .Select(t =>
            {
                bool isFallback = t.DataSource.Contains("fallback", StringComparison.OrdinalIgnoreCase);
                return ParseWeather(t.Output, t.DataSource, isFallback);
            })
            .ToList();
    }

    private static NormalizedWeatherData ParseWeather(string text, string dataSource, bool isFallback)
    {
        var location = ExtractField(text, @"Location:\s*(.+)") ?? "";
        var condition = ExtractField(text, @"Condition:\s*(.+)") ?? "";
        var temperature = ExtractField(text, @"Temperature:\s*(.+)") ?? "";
        var wind = ExtractField(text, @"Wind:\s*(.+)") ?? "";
        var precipitation = ExtractField(text, @"Precipitation:\s*(.+)") ?? "";
        var visibility = ExtractField(text, @"Visibility:\s*(.+)") ?? "";
        var riskFull = ExtractField(text, @"Disruption risk:\s*(.+)") ?? "";
        var severeAlert = ExtractField(text, @"SEVERE ALERT:\s*(.+)");

        var riskLevel = riskFull.Split('—', '–', '-')[0].Trim().ToUpperInvariant();
        var riskDescription = riskFull;

        return new NormalizedWeatherData
        {
            Location = location.Trim(),
            Condition = condition.Trim(),
            Temperature = temperature.Trim(),
            Wind = wind.Trim(),
            Precipitation = precipitation.Trim(),
            Visibility = visibility.Trim(),
            RiskLevel = riskLevel,
            RiskDescription = riskDescription.Trim(),
            SevereAlert = severeAlert?.Trim(),
            DataSource = dataSource,
            IsFromFallback = isFallback
        };
    }

    // ── Fact classification ───────────────────────────────────────────

    /// <summary>
    /// Only <paramref name="primaryFlights"/> (FlightLookupByNumber) and <paramref name="cleanAlternatives"/>
    /// — never duplicate route-search rows for the same flight number as the user's primary flight.
    /// </summary>
    private static void ClassifyFlightFacts(
        IReadOnlyList<NormalizedFlightData> primaryFlights,
        IReadOnlyList<NormalizedFlightData> cleanAlternatives,
        string intent,
        List<string> verified, List<string> unverified)
    {
        void ClassifyOne(NormalizedFlightData f, string roleLabel)
        {
            var src = f.IsFromFallback ? " (simulated data)" : "";
            var effectiveStatus = InferEffectiveStatus(f);
            var prefix = string.IsNullOrEmpty(roleLabel) ? "" : $"{roleLabel} ";

            switch (f.DataQuality)
            {
                case FlightDataQuality.Full:
                    verified.Add(
                        $"{prefix}Flight {f.FlightNumber} ({f.Airline}): {effectiveStatus}, " +
                        $"route {f.Route}" +
                        (f.ActualDeparture is not null ? $", actual departure {f.ActualDeparture}" : "") +
                        (f.ActualArrival is not null ? $", actual arrival {f.ActualArrival}" : "") +
                        (f.DelayMinutes is not null ? $", delay {f.DelayMinutes} minutes" : "") +
                        (f.Gate is not null ? $", gate {f.Gate}" : "") +
                        src);
                    break;

                case FlightDataQuality.ScheduledOnly:
                    verified.Add(
                        $"{prefix}Flight {f.FlightNumber} ({f.Airline}): status is \"{f.Status}\", " +
                        $"route {f.Route}, scheduled departure {f.ScheduledDeparture}, " +
                        $"scheduled arrival {f.ScheduledArrival}{src}");
                    unverified.Add(
                        $"Flight {f.FlightNumber}: only scheduled times available — " +
                        "actual departure/arrival times have not been confirmed");
                    break;

                case FlightDataQuality.Unavailable:
                    unverified.Add($"{prefix}Flight {f.FlightNumber}: no timing data available{src}");
                    break;
            }
        }

        foreach (var f in primaryFlights)
            ClassifyOne(f, "[PRIMARY — FlightLookupByNumber]");

        foreach (var f in cleanAlternatives)
            ClassifyOne(f, "[ALTERNATIVE — filtered route search]");

        var evidenceCount = primaryFlights.Count + cleanAlternatives.Count;
        if (evidenceCount == 0 && intent is "flight_cancellation" or "flight_delay" or "missed_connection")
        {
            unverified.Add("No flight data was retrieved — flight status is unknown");
        }

        bool anyCancelled = primaryFlights.Concat(cleanAlternatives).Any(f =>
            f.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase));
        if (intent == "flight_cancellation" && !anyCancelled && evidenceCount > 0)
        {
            unverified.Add(
                "The user reported a cancellation, but no flight tool returned a 'cancelled' status — " +
                "cancellation is unverified");
        }
    }

    private static void ClassifyWeatherFacts(
        List<NormalizedWeatherData> weather, string intent,
        List<string> verified, List<string> unverified)
    {
        foreach (var w in weather)
        {
            var src = w.IsFromFallback ? " (simulated data)" : "";
            verified.Add(
                $"Weather at {w.Location}: {w.Condition}, {w.Temperature}, " +
                $"wind {w.Wind}, visibility {w.Visibility}, " +
                $"disruption risk {w.RiskLevel}{src}");
            if (w.SevereAlert is not null)
                verified.Add($"SEVERE WEATHER ALERT at {w.Location}: {w.SevereAlert}");
        }

        if (weather.Count == 0 && intent is "weather_disruption" or "flight_cancellation")
        {
            unverified.Add("No weather data was retrieved — weather impact is unknown");
        }

        bool allMinimal = weather.Count > 0 && weather.All(w =>
            w.RiskLevel is "MINIMAL" or "LOW");
        if (allMinimal && intent is "weather_disruption" or "flight_cancellation")
        {
            unverified.Add(
                "Weather risk is MINIMAL/LOW at all checked locations — " +
                "weather is unlikely to be the cause of the disruption");
        }
    }

    /// <summary>
    /// When the API returns status "Unknown" but actual times/gate exist,
    /// derive a meaningful status string instead of the misleading raw value.
    /// </summary>
    private static string InferEffectiveStatus(NormalizedFlightData f)
    {
        var raw = f.Status.Trim();

        if (!string.IsNullOrEmpty(raw) &&
            !raw.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            return $"status \"{raw}\"";
        }

        if (f.ActualDeparture is not null && f.ActualArrival is not null)
            return "has departed and arrived (actual times confirmed)";
        if (f.ActualDeparture is not null)
            return "has departed / en route (actual departure confirmed)";
        if (f.ScheduledDeparture is not null)
            return "scheduled (no confirmed departure yet)";

        return "status unknown";
    }

    // ── Serialisation helpers (for the LLM prompt) ────────────────────

    public static string SerializeForPrompt(VerifiedContext ctx)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("=== VERIFIED FACTS (from live tools) ===");
        sb.AppendLine("CRITICAL: Lines tagged [PRIMARY — FlightLookupByNumber] are the user's own flight — use ONLY those for the passenger's flight status/times.");
        sb.AppendLine("Do NOT use any other row for the same flight number (e.g. from route search history).");
        sb.AppendLine("Lines tagged [ALTERNATIVE — filtered route search] are rebooking options only (already time-filtered).");
        sb.AppendLine();
        if (ctx.VerifiedFacts.Count > 0)
            foreach (var fact in ctx.VerifiedFacts)
                sb.AppendLine($"• {fact}");
        else
            sb.AppendLine("(none)");

        sb.AppendLine();
        sb.AppendLine("=== POLICY CONTEXT (from knowledge base) ===");
        if (ctx.PolicyContext.Count > 0)
            foreach (var policy in ctx.PolicyContext)
                sb.AppendLine($"• {policy}");
        else
            sb.AppendLine("(none)");

        sb.AppendLine();
        sb.AppendLine("=== UNVERIFIED / UNKNOWN ===");
        if (ctx.UnverifiedFacts.Count > 0)
            foreach (var fact in ctx.UnverifiedFacts)
                sb.AppendLine($"• {fact}");
        else
            sb.AppendLine("(none)");

        sb.AppendLine();
        sb.AppendLine("=== SKIPPED STEPS ===");
        if (ctx.SkippedSteps.Count > 0)
            foreach (var step in ctx.SkippedSteps)
                sb.AppendLine($"• {step.StepDescription}: {step.Reason}");
        else
            sb.AppendLine("(none)");

        sb.AppendLine();
        sb.AppendLine("=== EXECUTED TOOLS ===");
        foreach (var tool in ctx.ExecutedTools)
            sb.AppendLine($"• {tool.ToolName}({tool.Input}): {(tool.Success ? "SUCCESS" : $"FAILED — {tool.ErrorMessage}")} [{tool.DataSource}]");

        var primaryFlightInputs = ctx.ExecutedTools
            .Where(t => t.ToolName == "FlightLookupByNumber" && t.Success && !string.IsNullOrWhiteSpace(t.Input))
            .Select(t => t.Input.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (primaryFlightInputs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== USER'S OWN FLIGHT NUMBER(S) — never offer these as rebooking alternatives ===");
            foreach (var p in primaryFlightInputs)
                sb.AppendLine($"• {p}");
        }

        if (ctx.HasAlternativeFlights && ctx.CleanAlternatives.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(
                "=== ALTERNATIVE FLIGHTS AVAILABLE (default: departures from request+1h through request+24h; " +
                "if empty, search widens to 48h, 72h, 96h, then 120h — codeshare-deduped, excludes user's own flight) ===");
            if (ctx.AlternativeWindowContextNote is not null)
                sb.AppendLine($"• Window note: {ctx.AlternativeWindowContextNote}");
            sb.AppendLine(
                $"• Horizon used: {ctx.AlternativeSearchHorizonHours} hours from request (default 24h unless widened). " +
                $"Found in default 24h window: {(ctx.AlternativesFoundInDefault24hWindow ? "yes" : "no")}.");
            sb.AppendLine($"FlightLookupByRoute SUCCEEDED. {ctx.CleanAlternatives.Count} unique alternative(s) found:");
            foreach (var f in ctx.CleanAlternatives.Take(6))
            {
                var status = InferEffectiveStatus(f);
                var line = $"• {f.FlightNumber} ({f.Airline}): {status}, route {f.Route}";
                if (f.ScheduledDeparture is not null)
                    line += $", scheduled departure {f.ScheduledDeparture}";
                if (f.Gate is not null)
                    line += $", gate {f.Gate}";
                if (f.CodesharePartners.Count > 0)
                    line += $" [also marketed as: {string.Join(", ", f.CodesharePartners)}]";
                sb.AppendLine(line);
            }
            if (ctx.CleanAlternatives.Count > 6)
                sb.AppendLine($"• ... and {ctx.CleanAlternatives.Count - 6} more alternative(s)");
        }
        else if (ctx.HasAlternativeFlights)
        {
            sb.AppendLine();
            sb.AppendLine("=== ALTERNATIVE FLIGHTS ===");
            var providerRowsButFiltered = ctx.UnverifiedFacts.Any(f =>
                f.Contains("Live route data returned flight record(s)", StringComparison.OrdinalIgnoreCase));
            if (providerRowsButFiltered)
            {
                sb.AppendLine(
                    "FlightLookupByRoute returned flight record(s) from the live provider, but none qualified in the forward-departure UTC window. " +
                    "Do not state that no alternative flights exist; say the live feed could not confirm bookable options in that window and suggest airline or booking sites.");
                if (ctx.AlternativeWindowContextNote is not null)
                    sb.AppendLine(ctx.AlternativeWindowContextNote);
            }
            else if (ctx.AlternativeWindowContextNote is not null)
                sb.AppendLine(ctx.AlternativeWindowContextNote);
            else
                sb.AppendLine(
                    "FlightLookupByRoute succeeded but no alternative flights matched after widening the search " +
                    "from 24h up to 120h (5 days) forward from your request (minimum departure: 1 hour after request).");
        }

        return sb.ToString();
    }

    // ── Regex helpers ─────────────────────────────────────────────────

    private static string? ExtractField(string text, string pattern, bool firstLineOnly = false)
    {
        var input = firstLineOnly
            ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""
            : text;
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"Route:\s*(\S+)\s*→\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex RouteRegex();

    [GeneratedRegex(@"Flight\s+([A-Z]{2}\s?\d{1,4})", RegexOptions.IgnoreCase)]
    private static partial Regex FlightNumberInOutputRegex();
}
