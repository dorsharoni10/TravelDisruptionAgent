using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;

namespace TravelDisruptionAgent.Application.Services;

public partial class GuardrailsService : IGuardrailsService
{
    private const int MaxOutputLength = 100_000;

    private readonly ILogger<GuardrailsService> _logger;

    private static readonly HashSet<string> BlockedPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ignore previous instructions",
        "ignore all instructions",
        "system prompt",
        "you are now",
        "pretend you are",
        "act as",
        "jailbreak",
    };

    public GuardrailsService(ILogger<GuardrailsService> logger)
    {
        _logger = logger;
    }

    public Task<GuardrailResult> ValidateInputAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Task.FromResult(new GuardrailResult(false, "Message cannot be empty."));

        if (userMessage.Length > 2000)
            return Task.FromResult(new GuardrailResult(false, "Message is too long (max 2000 characters)."));

        var lower = userMessage.ToLowerInvariant();
        foreach (var pattern in BlockedPatterns)
        {
            if (lower.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Input guardrail: prompt injection attempt detected");
                return Task.FromResult(new GuardrailResult(false,
                    "I can only help with travel disruption scenarios. Please describe your travel situation."));
            }
        }

        if (ExcessiveSpecialCharsRegex().IsMatch(userMessage))
        {
            _logger.LogWarning("Input guardrail: excessive special characters detected");
            return Task.FromResult(new GuardrailResult(false,
                "Please provide a clear description of your travel disruption scenario."));
        }

        _logger.LogDebug("Input guardrail passed");
        return Task.FromResult(new GuardrailResult(true));
    }

    public Task<GuardrailResult> ValidateOutputAsync(string agentResponse, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentResponse))
            return Task.FromResult(new GuardrailResult(false, "Generated response was empty."));

        if (agentResponse.Length > MaxOutputLength)
        {
            _logger.LogWarning("Output guardrail: response too long ({Length} chars)", agentResponse.Length);
            return Task.FromResult(new GuardrailResult(false, "Response exceeded maximum length."));
        }

        _logger.LogDebug("Output guardrail passed");
        return Task.FromResult(new GuardrailResult(true));
    }

    // ── Factual accuracy guardrail ────────────────────────────────────

    public FactualAccuracyResult ValidateFactualAccuracy(string responseText, VerifiedContext ctx)
    {
        var violations = new List<FactualViolation>();
        var text = responseText;

        // Rule 1: mentions alternative flights without FlightLookupByRoute
        if (!ctx.HasAlternativeFlights && MentionsAlternativeFlights(text))
        {
            text = FabricatedAlternativesRegex().Replace(text, match =>
            {
                return "We were unable to retrieve alternative flights — contact your airline directly for rebooking options.";
            });
            violations.Add(new FactualViolation(
                "NO_ROUTE_SEARCH",
                "Response mentions alternative flights but FlightLookupByRoute was not executed or returned no results",
                FactualViolationSeverity.Corrected));
            _logger.LogWarning("Factual guardrail [NO_ROUTE_SEARCH]: removed fabricated alternative flights");
        }

        // Rule 1b: claims "unable to retrieve alternative flights" but route search succeeded AND we have filtered alternatives
        if (ctx.HasAlternativeFlights && ctx.CleanAlternatives.Count > 0 && UnableToRetrieveAlternativesRegex().IsMatch(text))
        {
            var routeFlights = ctx.CleanAlternatives
                .Where(f => f.DataQuality != FlightDataQuality.Unavailable)
                .Select(f => f.FlightNumber)
                .Distinct()
                .Take(5)
                .ToList();
            var summary = routeFlights.Count > 0
                ? $"Alternative flights were found on this route, including: {string.Join(", ", routeFlights)}. Review the flight data above for details, or contact your airline for rebooking."
                : "Alternative flights were found on this route. Review the flight data above for details, or contact your airline for rebooking.";

            text = UnableToRetrieveAlternativesRegex().Replace(text, summary);
            violations.Add(new FactualViolation(
                "ALTERNATIVES_AVAILABLE_CONTRADICTION",
                "Response claims alternative flights could not be retrieved but FlightLookupByRoute succeeded",
                FactualViolationSeverity.Corrected));
            _logger.LogWarning("Factual guardrail [ALTERNATIVES_AVAILABLE_CONTRADICTION]: corrected false unavailability claim");
        }

        // Rule 1c: says "no alternatives found" when FlightLookupByRoute was never executed
        bool routeSearchExecuted = ctx.ExecutedTools.Any(t => t.ToolName == "FlightLookupByRoute");
        if (!routeSearchExecuted && NoAlternativesFoundRegex().IsMatch(text))
        {
            text = NoAlternativesFoundRegex().Replace(text,
                "Alternative flights were not searched for this query. If you need rebooking options, contact your airline directly.");
            violations.Add(new FactualViolation(
                "NO_SEARCH_FALSE_NEGATIVE",
                "Response claims no alternatives were found but FlightLookupByRoute was never executed",
                FactualViolationSeverity.Corrected));
            _logger.LogWarning("Factual guardrail [NO_SEARCH_FALSE_NEGATIVE]: corrected false 'no alternatives found' when no search was done");
        }

        // Rule 2: claims "cancellation due to weather" without evidence
        if (!ctx.WeatherIsCausal &&
            CausedByWeatherRegex().IsMatch(text))
        {
            text = CausedByWeatherRegex().Replace(text,
                "due to a cause that has not yet been confirmed (weather conditions are currently minimal/low risk at checked locations)");
            violations.Add(new FactualViolation(
                "WEATHER_NOT_CAUSAL",
                "Response attributes disruption to weather but weather risk is MINIMAL/LOW",
                FactualViolationSeverity.Corrected));
            _logger.LogWarning("Factual guardrail [WEATHER_NOT_CAUSAL]: corrected unverified weather attribution");
        }

        // Rule 3: claims "no live flight data" or "could not verify status" when actual times exist
        if (ctx.HasActualTimes &&
            NoLiveDataRegex().IsMatch(text))
        {
            text = NoLiveDataRegex().Replace(text,
                "Live flight data including actual departure/arrival times was retrieved successfully.");
            violations.Add(new FactualViolation(
                "DATA_AVAILABLE_CONTRADICTION",
                "Response claims flight data is unavailable but actual times were returned by tools",
                FactualViolationSeverity.Corrected));
            _logger.LogWarning("Factual guardrail [DATA_AVAILABLE_CONTRADICTION]: corrected false unavailability claim");
        }

        // Rule 3b: claims "could not verify the status" when actual times/gate exist
        if (ctx.HasActualTimes && CouldNotVerifyStatusRegex().IsMatch(text))
        {
            text = CouldNotVerifyStatusRegex().Replace(text,
                "The official flight status label was not provided by the data source, " +
                "however actual departure times and gate information were confirmed — " +
                "the flight appears to have departed as scheduled.");
            violations.Add(new FactualViolation(
                "STATUS_PARTIAL_DATA",
                "Response claims status could not be verified but actual departure/gate data exists",
                FactualViolationSeverity.Corrected));
            _logger.LogWarning("Factual guardrail [STATUS_PARTIAL_DATA]: clarified partial status with available data");
        }

        // Rule 4: confident claims about unverified cancellation
        if (!ctx.CancellationVerified &&
            ctx.Intent == "flight_cancellation" &&
            ConfidentCancellationRegex().IsMatch(text) &&
            !text.Contains("could not be confirmed", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("could not be verified", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("unverified", StringComparison.OrdinalIgnoreCase))
        {
            text += "\n\n> **Verification note:** The flight cancellation status could not be confirmed through our data sources. " +
                    "Please verify directly with your airline before taking action based on this information.";
            violations.Add(new FactualViolation(
                "UNVERIFIED_CANCELLATION",
                "Response discusses cancellation as fact but no tool confirmed a cancelled status",
                FactualViolationSeverity.Warning));
            _logger.LogWarning("Factual guardrail [UNVERIFIED_CANCELLATION]: added verification disclaimer");
        }

        // Rule 5: references flight numbers not found in evidence (primary + filtered alternatives only)
        var knownFlights = ctx.EvidenceFlights.Select(f => f.FlightNumber.ToUpperInvariant()).ToHashSet();
        if (knownFlights.Count > 0)
        {
            var mentioned = FlightNumberInTextRegex().Matches(text)
                .Select(m => m.Value.ToUpperInvariant().Replace(" ", ""))
                .Distinct()
                .Where(fn => !knownFlights.Contains(fn))
                .ToList();
            if (mentioned.Count > 0)
            {
                violations.Add(new FactualViolation(
                    "UNKNOWN_FLIGHT_REFERENCED",
                    $"Response mentions flight(s) {string.Join(", ", mentioned)} not found in any tool result",
                    FactualViolationSeverity.Warning));
                _logger.LogWarning("Factual guardrail [UNKNOWN_FLIGHT_REFERENCED]: {Flights}", string.Join(", ", mentioned));
            }
        }

        // Rule 6: references a tool that was never executed
        var executedToolNames = ctx.ExecutedTools.Select(t => t.ToolName).ToHashSet();
        if (!executedToolNames.Contains("WeatherLookup") &&
            Regex.IsMatch(text, @"(weather (data|information|conditions) (show|indicate|confirm))", RegexOptions.IgnoreCase))
        {
            violations.Add(new FactualViolation(
                "UNREFERENCED_TOOL",
                "Response references weather data as fact, but WeatherLookup was never executed",
                FactualViolationSeverity.Warning));
            _logger.LogWarning("Factual guardrail [UNREFERENCED_TOOL]: weather data referenced without execution");
        }

        bool passed = violations.All(v => v.Severity != FactualViolationSeverity.Blocked);

        if (violations.Count == 0)
            _logger.LogDebug("Factual accuracy guardrail passed — no violations");
        else
            _logger.LogInformation("Factual accuracy guardrail: {Count} violation(s) detected, passed={Passed}",
                violations.Count, passed);

        return new FactualAccuracyResult(passed, text, violations);
    }

    // ── Detection helpers ─────────────────────────────────────────────

    private static bool MentionsAlternativeFlights(string text) =>
        AlternativeFlightsRegex().IsMatch(text) ||
        RecommendSpecificFlightRegex().IsMatch(text);

    [GeneratedRegex(@"[^a-zA-Z0-9\s.,!?;:'\-()/@#$%&*]{10,}")]
    private static partial Regex ExcessiveSpecialCharsRegex();

    [GeneratedRegex(@"(alternative|other available)\s+(flight|option)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex AlternativeFlightsRegex();

    [GeneratedRegex(@"rebook\s+(on|to)\s+flight\s+[A-Z]{2}\s?\d", RegexOptions.IgnoreCase)]
    private static partial Regex RecommendSpecificFlightRegex();

    [GeneratedRegex(@"(here are|found|available|consider)\s+(the\s+|some\s+)?(following\s+)?alternative\s+(flight|option)s?[^.]*\.", RegexOptions.IgnoreCase)]
    private static partial Regex FabricatedAlternativesRegex();

    [GeneratedRegex(@"(unable to retrieve|could not retrieve|were not able to retrieve|could not.*bring)\s+(alternative|other)\s+(flight|option)s?[^.]*", RegexOptions.IgnoreCase)]
    private static partial Regex UnableToRetrieveAlternativesRegex();

    [GeneratedRegex(@"no\s+(same[- ]day\s+)?alternative(s| flight)?\s+(were|was|have been)\s+(found|available|retrieved)[^.]*\.?", RegexOptions.IgnoreCase)]
    private static partial Regex NoAlternativesFoundRegex();

    [GeneratedRegex(@"(could not verify|could not be verified|unable to verify|cannot verify)\s+(the\s+)?(status|flight status)[^.]*\.", RegexOptions.IgnoreCase)]
    private static partial Regex CouldNotVerifyStatusRegex();

    [GeneratedRegex(@"(due to|caused by|because of|result of)\s+(severe\s+|adverse\s+)?weather", RegexOptions.IgnoreCase)]
    private static partial Regex CausedByWeatherRegex();

    [GeneratedRegex(@"real[- ]time\s+(flight\s+)?data\s+(was|is|were)\s+(not\s+available|unavailable)[^.]*\.", RegexOptions.IgnoreCase)]
    private static partial Regex NoLiveDataRegex();

    [GeneratedRegex(@"\b(has been|was|is)\s+cancel", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidentCancellationRegex();

    [GeneratedRegex(@"\b([A-Z]{2})\s?(\d{1,4})\b")]
    private static partial Regex FlightNumberInTextRegex();
}
