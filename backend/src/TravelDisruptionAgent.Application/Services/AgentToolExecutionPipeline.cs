using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Utilities;
using TravelDisruptionAgent.Domain.Enums;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>
/// Legacy tool execution, route search emission, and plan backfill for <see cref="AgentOrchestrator"/>.
/// </summary>
public sealed class AgentToolExecutionPipeline
{
    private readonly IToolExecutionCoordinator _toolCoordinator;
    private readonly ILogger<AgentToolExecutionPipeline> _logger;
    private readonly OrchestrationOptions _orchestrationOptions;

    public AgentToolExecutionPipeline(
        IToolExecutionCoordinator toolCoordinator,
        IOptions<OrchestrationOptions> orchestrationOptions,
        ILogger<AgentToolExecutionPipeline> logger)
    {
        _toolCoordinator = toolCoordinator;
        _orchestrationOptions = orchestrationOptions.Value;
        _logger = logger;
    }

    public async Task ExecuteLegacyToolsAsync(
        string userMessage,
        IReadOnlyList<ChatMessage> priorTurns,
        string intent,
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> selfCorrectionSteps,
        Func<AgentStepType, string, string, Task> emit,
        DateTime requestTimeUtc,
        CancellationToken ct)
    {
        var curFlights = OrchestrationParseHelpers.ExtractFlightNumbers(userMessage);
        var curLocs = OrchestrationParseHelpers.ExtractLocations(userMessage);
        var signalText = ConversationToolExtraction.BuildToolSignalText(
            userMessage, priorTurns, curFlights.Count > 0, curLocs.Count >= 2);

        var flightNumbers = OrchestrationParseHelpers.ExtractFlightNumbers(signalText);
        var locations = OrchestrationParseHelpers.ExtractLocations(signalText);
        var explicitDate = OrchestrationParseHelpers.ExtractDate(userMessage) ??
                           OrchestrationParseHelpers.ExtractDate(signalText);
        var date = explicitDate ?? requestTimeUtc.Date;

        foreach (var fn in flightNumbers)
        {
            await emit(AgentStepType.ToolCall, "Flight Lookup", $"Looking up flight {fn}...");
            var result = await _toolCoordinator.LookupFlightByNumberAsync(fn, date, ct);
            toolResults.Add(result);
            await emit(AgentStepType.ToolCall,
                result.Success ? "Flight Found" : "Flight Not Found", result.Output);

            if (!result.Success && result.ErrorMessage == "Flight not found" && locations.Count >= 2 &&
                UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(userMessage) &&
                !RouteLookupAlreadyExecuted(toolResults, locations[0], locations[1], date))
            {
                var sc = new SelfCorrectionStep(
                    $"Flight {fn} not found by number",
                    "Attempting route-based search as fallback",
                    "Searching alternative flights by route",
                    DateTime.UtcNow);
                selfCorrectionSteps.Add(sc);
                await emit(AgentStepType.SelfCorrection, sc.Issue, sc.Action);

                await ExecuteRouteSearchAsync(
                    emit, toolResults, locations[0], locations[1], date, requestTimeUtc, ct);
            }
        }

        if (explicitDate is null)
        {
            var primaryDay = OrchestrationParseHelpers.TryExtractDepartureCalendarDateFromPrimaryFlightTool(toolResults);
            if (primaryDay.HasValue)
                date = primaryDay.Value;
        }

        if (locations.Count >= 2 &&
            UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(userMessage) &&
            !RouteLookupAlreadyExecuted(toolResults, locations[0], locations[1], date))
        {
            await emit(AgentStepType.ToolCall, "Route Search",
                $"Searching flights {locations[0]} → {locations[1]}...");
            await ExecuteRouteSearchAsync(
                emit, toolResults, locations[0], locations[1], date, requestTimeUtc, ct);
        }

        var weatherLocations = new List<string>();
        var weatherIntent = intent.Contains("weather", StringComparison.OrdinalIgnoreCase);
        if (weatherIntent)
        {
            if (locations.Count > 0)
                weatherLocations.AddRange(locations.Take(2));
            if (weatherLocations.Count == 0 && intent == "weather_disruption")
                weatherLocations.AddRange(OrchestrationParseHelpers.ExtractWeatherLocationNames(userMessage).Take(2));
        }
        else if (UserToolRequestSignals.ExplicitlyAsksForWeather(userMessage))
        {
            if (locations.Count > 0)
                weatherLocations.AddRange(locations.Take(2));
            if (weatherLocations.Count == 0 && flightNumbers.Count > 0)
            {
                var flightData = toolResults.Where(t => t.ToolName.Contains("Flight") && t.Success).ToList();
                if (flightData.Count > 0)
                {
                    var airportMatches = OrchestrationParseHelpers.ThreeLetterCodeRegex().Matches(flightData[0].Output);
                    weatherLocations.AddRange(
                        airportMatches.Select(m => m.Value).Distinct().Take(2));
                }
            }
        }

        foreach (var loc in weatherLocations)
        {
            await emit(AgentStepType.ToolCall, "Weather Check", $"Checking weather at {loc}...");
            var result = await _toolCoordinator.LookupWeatherAsync(loc, ct);
            toolResults.Add(result);
            await emit(AgentStepType.ToolCall,
                result.Success ? "Weather Retrieved" : "Weather Unavailable",
                result.Output);
        }

        if (toolResults.Count == 0)
        {
            await emit(AgentStepType.ToolCall, "No Data",
                "Could not extract specific flight or location information. Providing general guidance.");
        }
    }

    /// <summary>Runs live <see cref="FlightLookupByRoute"/> when the plan requires it but no successful route row exists.</summary>
    public async Task<PlanValidationResult> TryApplyRouteBackfillAsync(
        PlanValidationResult validation,
        IReadOnlyList<string> planSteps,
        string workflowIntent,
        string userMessage,
        IReadOnlyList<ChatMessage> priorTurns,
        List<ToolExecutionResult> toolResults,
        List<SelfCorrectionStep> selfCorrectionSteps,
        Func<AgentStepType, string, string, Task> emit,
        DateTime requestTime,
        CancellationToken cancellationToken)
    {
        if (!validation.MissingTools.Contains("FlightLookupByRoute") ||
            toolResults.Any(t => t.ToolName == "FlightLookupByRoute" && t.Success))
            return validation;

        var backfillSignal = ConversationToolExtraction.BuildToolSignalText(
            userMessage,
            priorTurns,
            OrchestrationParseHelpers.ExtractFlightNumbers(userMessage).Count > 0,
            OrchestrationParseHelpers.ExtractLocations(userMessage).Count >= 2);
        var locations = OrchestrationParseHelpers.ExtractLocations(backfillSignal);
        if (locations.Count < 2)
        {
            var airportCodes = toolResults
                .Where(t => t.ToolName.Contains("Flight") && t.Success)
                .SelectMany(t => OrchestrationParseHelpers.ThreeLetterCodeRegex().Matches(t.Output).Select(m => m.Value))
                .Distinct()
                .Where(c => !OrchestrationParseHelpers.CommonNonAirportWords.Contains(c))
                .ToList();
            if (airportCodes.Count >= 2)
                locations = airportCodes.Take(2).ToList();
        }

        if (locations.Count < 2)
        {
            selfCorrectionSteps.Add(new SelfCorrectionStep(
                "Plan required FlightLookupByRoute but no origin/destination could be determined",
                "Cannot backfill — insufficient location data",
                "Alternative flights will not be included in the recommendation",
                DateTime.UtcNow));
            return validation;
        }

        var date = OrchestrationParseHelpers.ExtractDate(userMessage) ??
                   OrchestrationParseHelpers.ExtractDate(backfillSignal) ?? DateTime.Today;
        await emit(AgentStepType.ToolCall, "Route Search (Backfill)",
            $"Plan required alternative flight search — executing {locations[0]} → {locations[1]}...");
        var routeResult = await _toolCoordinator.LookupFlightByRouteAsync(
            locations[0], locations[1], date, cancellationToken);
        toolResults.Add(routeResult);
        string routeBody;
        string routeTitle;
        if (routeResult.Success)
        {
            var (display, diag) = VerifiedContextBuilder.BuildFilteredRouteDisplay(
                routeResult.Output,
                toolResults,
                requestTime,
                routeResult.DataSource,
                routeResult.DataSource.Contains("fallback", StringComparison.OrdinalIgnoreCase));
            routeBody = display;
            routeTitle = OrchestrationTraceLogger.ResolveRouteSearchStepTitle(routeResult.Output, diag);
            OrchestrationTraceLogger.LogRouteAlternativePipeline(
                _logger,
                diag,
                VerifiedContextBuilder.ExtractPrimaryFlightNumbers(toolResults),
                _orchestrationOptions.RedactSensitiveOrchestrationLogs);
        }
        else
        {
            routeBody = routeResult.Output;
            routeTitle = "Route Search: No Results";
        }

        await emit(AgentStepType.ToolCall, routeTitle, routeBody);

        selfCorrectionSteps.Add(new SelfCorrectionStep(
            "Plan required FlightLookupByRoute but it was not executed",
            "Backfilled route search before generating recommendation",
            routeResult.Success ? "Alternative flights found" : "No alternative flights available",
            DateTime.UtcNow));

        return PlanExecutionValidator.Validate(planSteps, toolResults, workflowIntent, userMessage);
    }

    private async Task ExecuteRouteSearchAsync(
        Func<AgentStepType, string, string, Task> emit,
        List<ToolExecutionResult> toolResults,
        string origin,
        string destination,
        DateTime date,
        DateTime requestTimeUtc,
        CancellationToken ct)
    {
        var routeResult = await _toolCoordinator.LookupFlightByRouteAsync(origin, destination, date, ct);
        toolResults.Add(routeResult);
        if (routeResult.Success)
        {
            var (display, diag) = VerifiedContextBuilder.BuildFilteredRouteDisplay(
                routeResult.Output,
                toolResults,
                requestTimeUtc,
                routeResult.DataSource,
                routeResult.DataSource.Contains("fallback", StringComparison.OrdinalIgnoreCase));
            OrchestrationTraceLogger.LogRouteAlternativePipeline(
                _logger,
                diag,
                VerifiedContextBuilder.ExtractPrimaryFlightNumbers(toolResults),
                _orchestrationOptions.RedactSensitiveOrchestrationLogs);
            var title = OrchestrationTraceLogger.ResolveRouteSearchStepTitle(routeResult.Output, diag);
            await emit(AgentStepType.ToolCall, title, display);
        }
        else
            await emit(AgentStepType.ToolCall, "Route Search: No Results", routeResult.Output);
    }

    private static bool RouteLookupAlreadyExecuted(
        List<ToolExecutionResult> toolResults,
        string origin,
        string destination,
        DateTime date)
    {
        var expected = $"{origin} → {destination} on {date:yyyy-MM-dd}";
        return toolResults.Any(t =>
            t.ToolName == "FlightLookupByRoute" &&
            string.Equals(t.Input, expected, StringComparison.OrdinalIgnoreCase));
    }
}
