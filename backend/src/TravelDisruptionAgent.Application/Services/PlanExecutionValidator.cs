using System.Text.RegularExpressions;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Utilities;

namespace TravelDisruptionAgent.Application.Services;

public static class PlanExecutionValidator
{
    public static PlanValidationResult Validate(
        IReadOnlyList<string> planSteps,
        List<ToolExecutionResult> toolResults,
        string intent,
        string? userMessage = null)
    {
        var executedTools = toolResults.Select(t => t.ToolName).Distinct().ToList();
        var issues = new List<PlanValidationIssue>();

        bool planRequiresRouteSearch = planSteps.Any(PlanStepImpliesRouteOrAlternatives);

        if (!planRequiresRouteSearch && userMessage is not null)
            planRequiresRouteSearch = UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(userMessage);

        bool planRequiresFlightLookup = planSteps.Any(s =>
            s.Contains("flight", StringComparison.OrdinalIgnoreCase) &&
            (s.Contains("look", StringComparison.OrdinalIgnoreCase) ||
             s.Contains("status", StringComparison.OrdinalIgnoreCase) ||
             s.Contains("check", StringComparison.OrdinalIgnoreCase)));

        var planMentionsWeather = planSteps.Any(s => s.Contains("weather", StringComparison.OrdinalIgnoreCase));
        var weatherIntent = intent.Contains("weather", StringComparison.OrdinalIgnoreCase);
        bool planRequiresWeather = planMentionsWeather &&
            (weatherIntent ||
             (userMessage is not null && UserToolRequestSignals.ExplicitlyAsksForWeather(userMessage)));

        bool hasRouteSearch = executedTools.Contains("FlightLookupByRoute");
        bool hasFlightLookup = executedTools.Contains("FlightLookupByNumber") || hasRouteSearch;
        bool hasWeather = executedTools.Contains("WeatherLookup");

        if (planRequiresRouteSearch && !hasRouteSearch)
        {
            issues.Add(new PlanValidationIssue(
                PlanIssueType.MissingRequiredTool,
                "FlightLookupByRoute",
                "Plan requires searching for alternative/rebooking flights but FlightLookupByRoute was not executed"));
        }

        if (planRequiresFlightLookup && !hasFlightLookup)
        {
            issues.Add(new PlanValidationIssue(
                PlanIssueType.MissingRequiredTool,
                "FlightLookup",
                "Plan requires flight lookup but no flight tool was executed"));
        }

        if (planRequiresWeather && !hasWeather)
        {
            issues.Add(new PlanValidationIssue(
                PlanIssueType.MissingRequiredTool,
                "WeatherLookup",
                "Plan requires weather check but WeatherLookup was not executed"));
        }

        // Detect contradictions
        DetectContradictions(toolResults, intent, issues);

        return new PlanValidationResult(
            issues,
            issues.Count == 0,
            issues.Where(i => i.Type == PlanIssueType.MissingRequiredTool)
                  .Select(i => i.ToolName!).ToList(),
            issues.Where(i => i.Type == PlanIssueType.Contradiction)
                  .Select(i => i.Description).ToList());
    }

    private static bool PlanStepImpliesRouteOrAlternatives(string s)
    {
        var x = s.ToLowerInvariant();
        if (x.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("rebooking", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("re-book", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("next available connecting flight", StringComparison.OrdinalIgnoreCase))
            return true;
        if (x.Contains("route", StringComparison.OrdinalIgnoreCase) &&
            (x.Contains("search", StringComparison.OrdinalIgnoreCase) ||
             x.Contains("same route", StringComparison.OrdinalIgnoreCase) ||
             x.Contains("along the route", StringComparison.OrdinalIgnoreCase)))
            return true;
        return x.Contains("search", StringComparison.OrdinalIgnoreCase) &&
               x.Contains("flight", StringComparison.OrdinalIgnoreCase) &&
               (x.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("route", StringComparison.OrdinalIgnoreCase));
    }

    private static void DetectContradictions(
        List<ToolExecutionResult> toolResults,
        string intent,
        List<PlanValidationIssue> issues)
    {
        var flightResults = toolResults
            .Where(t => t.ToolName.Contains("Flight") && t.Success).ToList();
        var weatherResults = toolResults
            .Where(t => t.ToolName == "WeatherLookup" && t.Success).ToList();

        bool flightExplicitlyCancelled = flightResults.Any(f =>
            Regex.IsMatch(f.Output, @"\bcancel", RegexOptions.IgnoreCase));

        bool weatherMinimal = weatherResults.All(w =>
            w.Output.Contains("MINIMAL", StringComparison.OrdinalIgnoreCase) ||
            w.Output.Contains("LOW", StringComparison.OrdinalIgnoreCase) ||
            w.Output.Contains("Clear", StringComparison.OrdinalIgnoreCase));

        bool weatherSevere = weatherResults.Any(w =>
            w.Output.Contains("HIGH", StringComparison.OrdinalIgnoreCase) ||
            w.Output.Contains("SEVERE", StringComparison.OrdinalIgnoreCase));

        if (intent == "flight_cancellation" && !flightExplicitlyCancelled && flightResults.Count > 0)
        {
            issues.Add(new PlanValidationIssue(
                PlanIssueType.Contradiction,
                null,
                "Intent is flight_cancellation but flight tool did not return an explicitly cancelled status — cancellation is unverified"));
        }

        if (weatherMinimal && weatherResults.Count > 0)
        {
            issues.Add(new PlanValidationIssue(
                PlanIssueType.WeatherNotCausal,
                null,
                "Weather risk is MINIMAL/LOW — weather should not be cited as the disruption cause"));
        }

        if (flightExplicitlyCancelled && weatherMinimal && weatherResults.Count > 0)
        {
            issues.Add(new PlanValidationIssue(
                PlanIssueType.Contradiction,
                null,
                "Flight is cancelled but weather is minimal — cancellation cause is not weather-related or is unknown"));
        }
    }
}

public record PlanValidationResult(
    List<PlanValidationIssue> Issues,
    bool IsFullyAligned,
    List<string> MissingTools,
    List<string> Contradictions);

public record PlanValidationIssue(
    PlanIssueType Type,
    string? ToolName,
    string Description);

public enum PlanIssueType
{
    MissingRequiredTool,
    Contradiction,
    WeatherNotCausal
}
