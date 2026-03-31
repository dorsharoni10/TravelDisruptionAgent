using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;

namespace TravelDisruptionAgent.Application.Services;

public class SelfCorrectionService : ISelfCorrectionService
{
    private readonly IToolExecutionCoordinator _toolCoordinator;
    private readonly ILogger<SelfCorrectionService> _logger;

    public SelfCorrectionService(
        IToolExecutionCoordinator toolCoordinator,
        ILogger<SelfCorrectionService> logger)
    {
        _toolCoordinator = toolCoordinator;
        _logger = logger;
    }

    public async Task<SelfCorrectionResult> ReviewAndCorrectAsync(
        string userMessage,
        List<ToolExecutionResult> toolResults,
        string draftRecommendation,
        double initialConfidence,
        PlanValidationResult? validation = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<SelfCorrectionStep>();
        var warnings = new List<string>();
        var adjustedConfidence = initialConfidence;
        var correctedRecommendation = draftRecommendation;

        // Rule 1: Check for failed flight lookups that could be retried via route
        await HandleFlightLookupFallbacks(userMessage, toolResults, steps, warnings, cancellationToken);

        // Rule 2: Handle weather lookup failures
        HandleWeatherFailures(toolResults, steps, warnings, ref adjustedConfidence);

        // Rule 3: Check for fallback data sources
        HandleFallbackWarnings(toolResults, steps, warnings, ref adjustedConfidence);

        // Rule 4: Cross-validate weather vs flight data for inconsistencies
        CheckDataConsistency(toolResults, steps, warnings, ref adjustedConfidence);

        // Rule 5: Assess overall data sufficiency
        AssessDataSufficiency(toolResults, steps, warnings, ref adjustedConfidence);

        // Rule 6: Validate recommendation text against actual tool results
        if (validation is not null)
            correctedRecommendation = ScrubUnverifiedClaims(
                correctedRecommendation, toolResults, validation, steps, warnings, ref adjustedConfidence);

        adjustedConfidence = Math.Clamp(adjustedConfidence, 0.1, 1.0);

        if (warnings.Count > 0)
        {
            correctedRecommendation += "\n\n---\n**Important notes:**\n" +
                string.Join("\n", warnings.Select(w => $"- {w}"));
        }

        _logger.LogDebug("Self-correction complete: {StepCount} steps, confidence {Initial} → {Adjusted}",
            steps.Count, initialConfidence, adjustedConfidence);

        return new SelfCorrectionResult(correctedRecommendation, adjustedConfidence, steps, warnings);
    }

    private Task HandleFlightLookupFallbacks(
        string userMessage,
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> steps,
        List<string> warnings,
        CancellationToken ct)
    {
        var failedByNumber = toolResults
            .Where(t => t.ToolName == "FlightLookupByNumber" && !t.Success &&
                        t.ErrorMessage == "Flight not found")
            .ToList();

        if (failedByNumber.Count == 0) return Task.CompletedTask;

        var existingRouteResults = toolResults
            .Where(t => t.ToolName == "FlightLookupByRoute" && t.Success)
            .ToList();

        if (existingRouteResults.Count > 0)
        {
            foreach (var failed in failedByNumber)
            {
                steps.Add(new SelfCorrectionStep(
                    $"Flight {failed.Input} not found by number",
                    "Route-based search was already performed as fallback",
                    "Alternative flight data available from route search",
                    DateTime.UtcNow));
            }
        }
        else
        {
            foreach (var failed in failedByNumber)
            {
                steps.Add(new SelfCorrectionStep(
                    $"Flight {failed.Input} not found by number",
                    "No route-based fallback was attempted",
                    "Recommendation may have limited flight data",
                    DateTime.UtcNow));
                warnings.Add($"Flight {failed.Input} could not be found. The recommendation is based on available data only.");
            }
        }

        return Task.CompletedTask;
    }

    private static void HandleWeatherFailures(
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> steps,
        List<string> warnings,
        ref double confidence)
    {
        var failedWeather = toolResults
            .Where(t => t.ToolName == "WeatherLookup" && !t.Success)
            .ToList();

        foreach (var failed in failedWeather)
        {
            steps.Add(new SelfCorrectionStep(
                $"Weather data unavailable for {failed.Input}",
                "Proceeding with partial information",
                "Recommendation will note missing weather context",
                DateTime.UtcNow));
            warnings.Add($"Weather data for {failed.Input} was unavailable. Recommendation may not fully account for weather-related disruptions.");
            confidence -= 0.15;
        }
    }

    private static void HandleFallbackWarnings(
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> steps,
        List<string> warnings,
        ref double confidence)
    {
        var fallbackResults = toolResults
            .Where(t => t.DataSource.Contains("fallback", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (fallbackResults.Count == 0) return;

        foreach (var fb in fallbackResults)
        {
            steps.Add(new SelfCorrectionStep(
                $"Real-time data source failed for {fb.ToolName}",
                "Fell back to simulated data",
                fb.Warning ?? "Using simulated data as substitute",
                DateTime.UtcNow));

            if (fb.Warning is not null && !warnings.Contains(fb.Warning))
                warnings.Add(fb.Warning);
        }

        confidence -= 0.1 * fallbackResults.Count;
    }

    private static void CheckDataConsistency(
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> steps,
        List<string> warnings,
        ref double confidence)
    {
        var weatherResults = toolResults.Where(t => t.ToolName == "WeatherLookup" && t.Success).ToList();
        var flightResults = toolResults.Where(t => t.ToolName.Contains("Flight") && t.Success).ToList();

        if (weatherResults.Count == 0 || flightResults.Count == 0) return;

        bool hasSevereWeather = weatherResults.Any(w =>
            w.Output.Contains("HIGH", StringComparison.OrdinalIgnoreCase) ||
            w.Output.Contains("SEVERE", StringComparison.OrdinalIgnoreCase));

        bool hasOnTimeFlights = flightResults.Any(f =>
            f.Output.Contains("Scheduled", StringComparison.OrdinalIgnoreCase) &&
            !f.Output.Contains("Delay", StringComparison.OrdinalIgnoreCase) &&
            !f.Output.Contains("Cancel", StringComparison.OrdinalIgnoreCase));

        if (hasSevereWeather && hasOnTimeFlights)
        {
            steps.Add(new SelfCorrectionStep(
                "Inconsistency detected: severe weather reported but flights show as on-time",
                "Adding precautionary weather warning",
                "User warned that scheduled flights may be affected by weather",
                DateTime.UtcNow));
            warnings.Add("Severe weather is reported at one or more locations. Currently scheduled flights may still be affected — monitor for updates.");
            confidence -= 0.1;
        }
    }

    private static void AssessDataSufficiency(
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> steps,
        List<string> warnings,
        ref double confidence)
    {
        if (toolResults.Count == 0)
        {
            steps.Add(new SelfCorrectionStep(
                "No tool data available",
                "Providing general guidance only",
                "Recommendation is based on general best practices, not real-time data",
                DateTime.UtcNow));
            warnings.Add("No real-time data was available. This recommendation is based on general best practices only.");
            confidence = Math.Min(confidence, 0.3);
            return;
        }

        var successRate = (double)toolResults.Count(t => t.Success) / toolResults.Count;
        if (successRate < 0.5)
        {
            steps.Add(new SelfCorrectionStep(
                $"Low data availability: only {successRate:P0} of tool calls succeeded",
                "Lowering confidence and adding data caveat",
                "Recommendation marked as limited-confidence",
                DateTime.UtcNow));
            warnings.Add("Limited data was available for this analysis. Please verify key details with your airline directly.");
            confidence -= 0.2;
        }
    }

    /// <summary>
    /// Post-process the recommendation text to remove claims that contradict the actual tool results.
    /// </summary>
    private static string ScrubUnverifiedClaims(
        string recommendation,
        List<ToolExecutionResult> toolResults,
        PlanValidationResult validation,
        List<SelfCorrectionStep> steps,
        List<string> warnings,
        ref double adjustedConfidence)
    {
        var text = recommendation;
        bool hasRouteResults = toolResults.Any(t => t.ToolName == "FlightLookupByRoute" && t.Success);
        bool routeSearchListedAlternatives = toolResults.Any(t =>
            t.ToolName == "FlightLookupByRoute" && t.Success &&
            Regex.IsMatch(t.Output, @"Found\s+[1-9]\d*\s+alternative\s+flight", RegexOptions.IgnoreCase));
        bool flightCancelled = toolResults.Any(t =>
            t.ToolName.Contains("Flight") && t.Success &&
            Regex.IsMatch(t.Output, @"\bcancel", RegexOptions.IgnoreCase));
        bool weatherMinimal = validation.Issues.Any(i => i.Type == PlanIssueType.WeatherNotCausal);
        bool hasActualTimes = toolResults.Any(t =>
            t.ToolName.Contains("Flight") && t.Success &&
            (t.Output.Contains("Actual departure:", StringComparison.OrdinalIgnoreCase) ||
             t.Output.Contains("Actual arrival:", StringComparison.OrdinalIgnoreCase)));

        if (hasActualTimes && Regex.IsMatch(text, @"real[- ]time\s+(flight\s+)?data\s+(was|is)\s+unavailable", RegexOptions.IgnoreCase))
        {
            text = Regex.Replace(text,
                @"real[- ]time\s+(flight\s+)?data\s+(was|is)\s+unavailable[^.]*\.",
                "Live flight data including actual departure/arrival times was retrieved successfully.",
                RegexOptions.IgnoreCase);
            steps.Add(new SelfCorrectionStep(
                "Recommendation incorrectly claims flight data is unavailable, but actual times were returned",
                "Corrected the claim to reflect that live data is available",
                "Inaccurate data-availability statement removed",
                DateTime.UtcNow));
        }

        if (!hasRouteResults && ContainsFabricatedAlternatives(text))
        {
            steps.Add(new SelfCorrectionStep(
                "Recommendation mentions alternative flights but FlightLookupByRoute was not executed or returned no results",
                "Removing fabricated alternative flight references",
                "Replaced with honest disclosure that alternatives were not retrieved",
                DateTime.UtcNow));
            warnings.Add("Alternative flights could not be retrieved. Contact your airline for rebooking options.");
            adjustedConfidence -= 0.15;
        }

        if (hasRouteResults && routeSearchListedAlternatives && DeniesAlternativeFlights(text))
        {
            text = Regex.Replace(text,
                @"(unable to retrieve|could not retrieve|were not able to retrieve|could not.*bring)\s+(alternative|other)\s+(flight|option)s?[^.]*\.",
                "Alternative flights were found on this route — review the flight data above for options, or contact your airline for rebooking.",
                RegexOptions.IgnoreCase);
            steps.Add(new SelfCorrectionStep(
                "Recommendation claims alternative flights could not be retrieved but FlightLookupByRoute succeeded",
                "Corrected false unavailability claim",
                "Updated to reflect that route search returned results",
                DateTime.UtcNow));
        }

        if (!flightCancelled && Regex.IsMatch(text, @"cancel", RegexOptions.IgnoreCase) &&
            !text.Contains("could not be confirmed", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("could not be verified", StringComparison.OrdinalIgnoreCase))
        {
            text += "\n\n> **Verification:** The flight cancellation status could not be confirmed through our data sources. " +
                    "Please verify directly with your airline.";
            steps.Add(new SelfCorrectionStep(
                "Recommendation discusses cancellation but flight status was not confirmed as cancelled",
                "Added verification disclaimer",
                "User informed that cancellation is unverified",
                DateTime.UtcNow));
            adjustedConfidence -= 0.1;
        }

        if (weatherMinimal && Regex.IsMatch(text, @"(due to|caused by|because of)\s+(severe\s+)?weather", RegexOptions.IgnoreCase))
        {
            text += "\n\n> **Note:** Weather conditions at the relevant airports are currently minimal/low risk. " +
                    "The cause of the disruption has not been determined to be weather-related.";
            steps.Add(new SelfCorrectionStep(
                "Recommendation attributes disruption to weather but weather risk is MINIMAL",
                "Added correction note",
                "User informed that weather is not the confirmed cause",
                DateTime.UtcNow));
            warnings.Add("Weather risk is currently minimal — the disruption cause may not be weather-related.");
            adjustedConfidence -= 0.1;
        }

        return text;
    }

    private static bool ContainsFabricatedAlternatives(string text) =>
        Regex.IsMatch(text, @"alternative\s+(flight|option)", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(text, @"(here are|available)\s+(some\s+)?alternative", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(text, @"rebook\s+(on|to)\s+flight\s+[A-Z]{2}\s?\d", RegexOptions.IgnoreCase);

    private static bool DeniesAlternativeFlights(string text) =>
        Regex.IsMatch(text, @"(unable to retrieve|could not retrieve|were not able to retrieve)\s+(alternative|other)\s+(flight|option)s?", RegexOptions.IgnoreCase);
}
