using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Utilities;

namespace TravelDisruptionAgent.Application.Services;

public class PlanningService : IPlanningService
{
    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<PlanningService> _logger;

    public PlanningService(
        IKernelFactory kernelFactory,
        ILogger<PlanningService> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    public async Task<AgentPlan> CreatePlanAsync(
        string userMessage,
        string intent,
        bool policyKnowledgeWillBeRetrieved,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (_kernelFactory.IsConfigured)
        {
            try
            {
                return await CreatePlanWithLlmAsync(
                    userMessage, intent, policyKnowledgeWillBeRetrieved, priorConversationContext, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM-based planning failed, using rule-based fallback");
                var fallback = CreateRuleBasedPlan(intent, policyKnowledgeWillBeRetrieved, userMessage);
                return fallback with { LlmFallbackReason = LlmErrorDescriptions.GetPublicMessage(ex) };
            }
        }

        return CreateRuleBasedPlan(intent, policyKnowledgeWillBeRetrieved, userMessage);
    }

    public AgentPlan CreateDeterministicPlan(string intent, bool policyKnowledgeWillBeRetrieved, string? userMessage = null) =>
        CreateRuleBasedPlan(intent, policyKnowledgeWillBeRetrieved, userMessage);

    private async Task<AgentPlan> CreatePlanWithLlmAsync(
        string userMessage,
        string intent,
        bool policyKnowledgeWillBeRetrieved,
        string? priorConversationContext,
        CancellationToken cancellationToken)
    {
        var kernel = _kernelFactory.CreateKernel();

        var priorBlock = string.IsNullOrWhiteSpace(priorConversationContext)
            ? ""
            : $"""

            Prior conversation (earlier turns only):
            {priorConversationContext.Trim()}

            """;

        var policyBlock = policyKnowledgeWillBeRetrieved
            ? """

            Company policy knowledge base:
            The pipeline will retrieve relevant company travel policy excerpts (rebooking rules, meal and hotel expense limits, compensation summaries, baggage and check-in guidance). When the user asks what the company covers (meals, hotels, vouchers, rights), plan steps that assume those excerpts will be available to the recommendation step — cite policy-based actions where appropriate. Do not default to generic "contact your organization for policy details" when the question can be answered from standard company travel policy documents that are being retrieved.
            """
            : "";

        const string planPromptCore = """
            You are a planning agent for a Travel Disruption Assistant.
            Create a short action plan (3-6 steps) to resolve the user's travel disruption.
            Write the plan in English only, regardless of the user's message language.

            Only include steps that use FlightLookupByRoute (alternatives), or WeatherLookup, if the user explicitly asked
            for other/replacement flights, rebooking, route search, or weather/forecast. If they only ask for status,
            times, or one flight — use FlightLookupByNumber only unless they clearly asked for more.

            Available tools:
            - FlightLookupByNumber: Look up flight status by flight number
            - FlightLookupByRoute: Search flights by origin, destination, and date
            - WeatherLookup: Check weather conditions at a location or airport
            """;

        const string planPromptFooter = """

            Format your response EXACTLY like this:
            GOAL: <one sentence goal>
            1. <step>
            2. <step>
            ...

            User intent: {{$intent}}
            User message: {{$input}}
            """;

        var prompt = planPromptCore + priorBlock + policyBlock + planPromptFooter;

        var result = await kernel.InvokePromptAsync(
            prompt,
            new KernelArguments { ["input"] = userMessage, ["intent"] = intent },
            cancellationToken: cancellationToken);

        return ParseLlmPlan(result.GetValue<string>() ?? "");
    }

    private static AgentPlan ParseLlmPlan(string llmResponse)
    {
        var lines = llmResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var goalLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("GOAL:", StringComparison.OrdinalIgnoreCase));
        var goal = goalLine?.Split(':', 2).LastOrDefault()?.Trim() ?? "Resolve travel disruption";

        var steps = lines
            .Where(l => Regex.IsMatch(l.TrimStart(), @"^\d+[\.\)]\s"))
            .Select(l => Regex.Replace(l.TrimStart(), @"^\d+[\.\)]\s*", "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (steps.Count == 0)
            steps = ["Analyze the situation", "Gather relevant data", "Generate recommendation"];

        return new AgentPlan(goal, steps);
    }

    private AgentPlan CreateRuleBasedPlan(string intent, bool policyKnowledgeWillBeRetrieved, string? userMessage = null)
    {
        _logger.LogDebug("Creating rule-based plan for intent: {Intent}", intent);

        var (goal, steps) = intent switch
        {
            "flight_cancellation" => (
                "Help user handle flight cancellation and find alternatives",
                new List<string>
                {
                    "Look up the cancelled flight status and details",
                    "Check weather conditions at departure and arrival airports",
                    "Search for alternative flights on the same route",
                    "Evaluate rebooking options based on availability",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Generate recommendation with best alternatives and company policy context"
                }),

            "flight_delay" => (
                "Assess flight delay impact and provide updated information",
                new List<string>
                {
                    "Look up current flight status and delay details",
                    "Check weather conditions that may affect the flight",
                    "Assess impact on connecting flights if applicable",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Generate recommendation with delay expectations and expense or voucher policy if relevant"
                }),

            "weather_disruption" => (
                "Assess weather impact on travel plans and recommend actions",
                new List<string>
                {
                    "Check current and forecast weather conditions at relevant locations",
                    "Look up affected flight statuses",
                    "Evaluate disruption risk level",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Generate recommendation with weather-aware alternatives and policy-backed disruption steps"
                }),

            "missed_connection" => (
                "Help user recover from missed connection",
                new List<string>
                {
                    "Look up the missed connecting flight details",
                    "Search for next available connecting flights",
                    "Check weather at connection airport",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Generate recommendation with rebooking options and company travel policy where applicable"
                }),

            "rebooking_request" => (
                "Find and recommend rebooking options",
                new List<string>
                {
                    "Look up original flight details",
                    "Search for alternative flights on the route",
                    "Check weather conditions along the route",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Compare options and generate recommendation using policy rules on fees and approvals"
                }),

            "baggage_issue" => (
                "Help user resolve baggage issue",
                new List<string>
                {
                    "Look up the relevant flight details",
                    "Check current flight and airport status",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Generate recommendation for baggage recovery with policy timing and claims guidance"
                }),

            _ => (
                "Analyze travel disruption and provide assistance",
                new List<string>
                {
                    "Analyze the travel disruption scenario",
                    "Check flight status if applicable",
                    "Check weather conditions if applicable",
                    PolicyStep(policyKnowledgeWillBeRetrieved),
                    "Generate recommendation"
                })
        };

        steps.RemoveAll(s => string.IsNullOrWhiteSpace(s));
        if (!policyKnowledgeWillBeRetrieved)
            steps.RemoveAll(s => s.StartsWith("Review company travel policy", StringComparison.OrdinalIgnoreCase));

        ApplyUserToolStepPreferences(steps, intent, userMessage);

        return new AgentPlan(goal, steps);
    }

    private static void ApplyUserToolStepPreferences(List<string> steps, string intent, string? userMessage)
    {
        var weatherIntent = intent.Contains("weather", StringComparison.OrdinalIgnoreCase);
        if (!weatherIntent && !UserToolRequestSignals.ExplicitlyAsksForWeather(userMessage))
            steps.RemoveAll(s => s.Contains("weather", StringComparison.OrdinalIgnoreCase));

        if (UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(userMessage))
            return;

        steps.RemoveAll(s =>
        {
            var x = s.ToLowerInvariant();
            return x.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
                   x.Contains("rebooking", StringComparison.OrdinalIgnoreCase) ||
                   x.Contains("re-book", StringComparison.OrdinalIgnoreCase) ||
                   x.Contains("next available connecting flight", StringComparison.OrdinalIgnoreCase) ||
                   (x.Contains("route", StringComparison.OrdinalIgnoreCase) &&
                    (x.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                     x.Contains("same route", StringComparison.OrdinalIgnoreCase) ||
                     x.Contains("along the route", StringComparison.OrdinalIgnoreCase))) ||
                   (x.Contains("search", StringComparison.OrdinalIgnoreCase) &&
                    x.Contains("flight", StringComparison.OrdinalIgnoreCase) &&
                    (x.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
                     x.Contains("route", StringComparison.OrdinalIgnoreCase)));
        });
    }

    private static string PolicyStep(bool policyKnowledgeWillBeRetrieved) =>
        policyKnowledgeWillBeRetrieved
            ? "Review company travel policy excerpts (meals, hotels, rebooking, rights) to be supplied to the recommendation step"
            : "";
}
