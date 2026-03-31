namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>
/// Structured, evidence-only context built from tool results and RAG
/// before the final recommendation is generated.
/// </summary>
public record VerifiedContext
{
    public required string Intent { get; init; }

    public required List<ExecutedToolSummary> ExecutedTools { get; init; }
    public required List<SkippedStep> SkippedSteps { get; init; }

    public required List<NormalizedFlightData> Flights { get; init; }
    public required List<NormalizedWeatherData> Weather { get; init; }

    public required List<string> VerifiedFacts { get; init; }
    public required List<string> UnverifiedFacts { get; init; }

    public required List<string> PolicyContext { get; init; }

    public required List<string> Contradictions { get; init; }

    /// <summary>Flights that may be cited as evidence: primary lookup(s) + filtered alternatives only (excludes raw route dump).</summary>
    public IEnumerable<NormalizedFlightData> EvidenceFlights
    {
        get
        {
            foreach (var p in Flights.Where(f => f.IsPrimaryLookup))
                yield return p;
            foreach (var a in CleanAlternatives)
                yield return a;
        }
    }

    public bool HasLiveFlightData => EvidenceFlights.Any(f => f.DataQuality != FlightDataQuality.Unavailable);
    public bool HasActualTimes => EvidenceFlights.Any(f => f.DataQuality == FlightDataQuality.Full);
    public bool HasAlternativeFlights => ExecutedTools.Any(t =>
        t.ToolName == "FlightLookupByRoute" && t.Success);
    public bool CancellationVerified => EvidenceFlights.Any(f =>
        f.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase));
    public bool WeatherIsCausal => Weather.Any(w =>
        w.RiskLevel is "HIGH" or "SEVERE" or "MODERATE");
    public bool CancellationReasonKnown =>
        CancellationVerified && (WeatherIsCausal || EvidenceFlights.Any(f =>
            !string.IsNullOrEmpty(f.Status) &&
            !f.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The user's own flight looked up by number (first match if several).</summary>
    public NormalizedFlightData? PrimaryFlight => Flights.FirstOrDefault(f => f.IsPrimaryLookup);

    /// <summary>
    /// Cleaned list of alternative flights: codeshare-deduped, excludes user's flight;
    /// departure window is <see cref="AlternativeSearchHorizonHours"/> from request (after progressive widening).
    /// </summary>
    public required List<NormalizedFlightData> CleanAlternatives { get; init; }

    /// <summary>Maximum forward hours used when alternatives were found (24, 48, 72, 96, or 120), or 120 if none found after full widen.</summary>
    public int AlternativeSearchHorizonHours { get; init; }

    /// <summary>True if at least one alternative fell in the default 24h horizon (request +1h .. +24h).</summary>
    public bool AlternativesFoundInDefault24hWindow { get; init; }

    /// <summary>
    /// When search widened past 24h: explains expansion. When no alternatives after 120h: explains full search.
    /// Null if alternatives were found in the default 24h window.
    /// </summary>
    public string? AlternativeWindowContextNote { get; init; }
}

public record ExecutedToolSummary(
    string ToolName,
    string Input,
    bool Success,
    string DataSource,
    string? ErrorMessage);

public record SkippedStep(
    string StepDescription,
    string Reason);

public record NormalizedFlightData
{
    public required string FlightNumber { get; init; }
    public string Airline { get; init; } = "";
    public string Status { get; init; } = "";
    public string Route { get; init; } = "";
    public string DepartureAirport { get; init; } = "";
    public string ArrivalAirport { get; init; } = "";
    public string? ScheduledDeparture { get; init; }
    public string? ScheduledArrival { get; init; }
    public string? ActualDeparture { get; init; }
    public string? ActualArrival { get; init; }
    public string? DelayMinutes { get; init; }
    public string? Gate { get; init; }
    public FlightDataQuality DataQuality { get; init; }
    public string DataSource { get; init; } = "";
    public bool IsFromFallback { get; init; }
    /// <summary>Whether this flight came from FlightLookupByNumber (the user's own flight).</summary>
    public bool IsPrimaryLookup { get; init; }
    /// <summary>Codeshare flight numbers that share the same physical aircraft.</summary>
    public List<string> CodesharePartners { get; init; } = [];
}

public record NormalizedWeatherData
{
    public required string Location { get; init; }
    public string Condition { get; init; } = "";
    public string Temperature { get; init; } = "";
    public string Wind { get; init; } = "";
    public string Precipitation { get; init; } = "";
    public string Visibility { get; init; } = "";
    public string RiskLevel { get; init; } = "";
    public string RiskDescription { get; init; } = "";
    public string? SevereAlert { get; init; }
    public string DataSource { get; init; } = "";
    public bool IsFromFallback { get; init; }
}

public enum FlightDataQuality
{
    Full,
    ScheduledOnly,
    Unavailable
}
