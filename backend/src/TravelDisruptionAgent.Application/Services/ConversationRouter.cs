using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TravelDisruptionAgent.Application.Constants;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Utilities;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>
/// Unified scope + routing: in/out of scope, granular intent, needs tools/RAG, and suggested RAG topics.
/// LLM-first when configured; otherwise semantic policy hints + structural signals (flight numbers, IATA pairs).
/// </summary>
public class ConversationRouter : IConversationRouter
{
    private static readonly string OutOfScopeMessage =
        "I'm a Travel Disruption Agent and can only help with travel disruption scenarios " +
        "such as flight cancellations, delays, weather disruptions, missed connections, rebooking requests, " +
        "and company business-travel policy (meals, expenses, rebooking rules, baggage and check-in guidance). " +
        "How can I help you with a travel issue?";

    private readonly IKernelFactory _kernelFactory;
    private readonly IRagService _ragService;
    private readonly IPlanningService _planningService;
    private readonly ILogger<ConversationRouter> _logger;

    public ConversationRouter(
        IKernelFactory kernelFactory,
        IRagService ragService,
        IPlanningService planningService,
        ILogger<ConversationRouter> logger)
    {
        _kernelFactory = kernelFactory;
        _ragService = ragService;
        _planningService = planningService;
        _logger = logger;
    }

    public async Task<ConversationRouteResult> RouteAsync(
        string userMessage,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Out(AgentIntents.OutOfScope, "Empty message");

        if (_kernelFactory.IsConfigured)
        {
            try
            {
                var llm = await RouteWithLlmAsync(userMessage, priorConversationContext, cancellationToken);
                return await RefineWithSignalsAsync(userMessage, llm, llmFallback: null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Conversation routing LLM failed — using semantic + signal fallback");
                var fallback = await RouteWithSignalsAsync(userMessage, cancellationToken);
                return fallback with
                {
                    LlmFallbackReason = LlmErrorDescriptions.GetPublicMessage(ex)
                };
            }
        }

        return await RouteWithSignalsAsync(userMessage, cancellationToken);
    }

    public async Task<RouteAndPlanResult> RouteAndPlanAsync(
        string userMessage,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            var emptyRoute = Out(AgentIntents.OutOfScope, "Empty message");
            return new RouteAndPlanResult(
                emptyRoute,
                _planningService.CreateDeterministicPlan(AgentIntents.OutOfScope, false, userMessage),
                null);
        }

        if (!_kernelFactory.IsConfigured)
        {
            var route = await RouteWithSignalsAsync(userMessage, cancellationToken);
            var plan = _planningService.CreateDeterministicPlan(
                route.Intent, route.NeedsRag || route.NeedsTools, userMessage);
            return new RouteAndPlanResult(route, plan, null);
        }

        try
        {
            var raw = await RouteAndPlanWithLlmAsync(userMessage, priorConversationContext, cancellationToken);
            SplitRoutingAndPlan(raw, out var routingBody, out var planBody);

            if (string.IsNullOrWhiteSpace(routingBody) ||
                !routingBody.Contains("SCOPE:", StringComparison.OrdinalIgnoreCase))
            {
                return await FallbackRouteAndPlanAsync(
                    userMessage, "Unified response missing valid ROUTING section.", cancellationToken);
            }

            var parsedRoute = ParseLlmRoute(routingBody, userMessage);
            var refinedRoute = await RefineWithSignalsAsync(userMessage, parsedRoute, null, cancellationToken);

            string? unifiedFallback = null;
            var plan = ParsePlanSection(planBody);
            if (plan.Steps.Count == 0)
            {
                plan = _planningService.CreateDeterministicPlan(
                    refinedRoute.Intent, refinedRoute.NeedsRag || refinedRoute.NeedsTools, userMessage);
                unifiedFallback = "Unified PLAN section missing or empty — deterministic plan used.";
            }

            return new RouteAndPlanResult(refinedRoute, plan, unifiedFallback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unified route+plan LLM failed — signals + deterministic plan");
            return await FallbackRouteAndPlanAsync(
                userMessage, $"Unified route+plan failed: {LlmErrorDescriptions.GetPublicMessage(ex)}",
                cancellationToken);
        }
    }

    private async Task<RouteAndPlanResult> FallbackRouteAndPlanAsync(
        string userMessage,
        string reason,
        CancellationToken cancellationToken)
    {
        var route = await RouteWithSignalsAsync(userMessage, cancellationToken);
        var plan = _planningService.CreateDeterministicPlan(
            route.Intent, route.NeedsRag || route.NeedsTools, userMessage);
        return new RouteAndPlanResult(route, plan, reason);
    }

    private async Task<string> RouteAndPlanWithLlmAsync(
        string userMessage,
        string? priorConversationContext,
        CancellationToken cancellationToken)
    {
        var kernel = _kernelFactory.CreateKernel();

        var priorBlock = string.IsNullOrWhiteSpace(priorConversationContext)
            ? ""
            : $"""

            Prior conversation (earlier turns only; the latest user message is given separately below):
            {priorConversationContext.Trim()}

            """;

        const string unifiedPrompt = """
            You route and plan for a Travel Disruption Assistant in ONE response (saves latency — do not omit sections).

            ROUTING rules — IN_SCOPE includes:
            - Live flight status, delays, cancellations, diversions, gates, connections, rebooking, weather at airports
            - Travel disruption assistance (stranded, delayed, cancelled)
            - Company business travel policy: meals, hotels, expenses, reimbursement, rebooking, baggage/check-in, compensation summaries

            OUT_OF_SCOPE: jokes, coding, sports, trivia, non-travel topics.

            PLAN rules:
            - 3–6 numbered steps using tools when relevant.
            - Include FlightLookupByRoute (alternatives) or WeatherLookup only if the user explicitly asked for other/replacement flights, rebooking, route search, or weather/forecast. Status-only questions → FlightLookupByNumber only.
            - When NEEDS_RAG is yes, steps may assume company policy excerpts will be retrieved later.
            - Write the plan in English only, regardless of user language.

            Respond using EXACTLY this structure (keep line prefixes):

            ROUTING
            SCOPE: IN_SCOPE or OUT_OF_SCOPE
            INTENT: flight_operational | weather_operational | disruption_assistance | company_travel_policy | reimbursement_expenses | baggage_checkin_policy | rebooking_policy | missed_connection | strike_disruption | flight_diversion | mixed_disruption | out_of_scope
            NEEDS_TOOLS: yes or no
            NEEDS_RAG: yes or no
            INTERNAL_ONLY: yes or no
            RAG_TOPICS: comma-separated short labels or none
            REASON: one concise English sentence

            PLAN
            GOAL: <one sentence goal>
            1. <step>
            2. <step>
            ...

            NEEDS_TOOLS yes if flight lookup (by number) is required, or route/weather APIs are required because the user explicitly asked for alternatives/rebooking/route options or weather.
            NEEDS_RAG yes if company policy / reimbursement / rights from documents is needed.
            INTERNAL_ONLY yes only for vague in-domain tips with no tools and no policy retrieval.

            User message: {{$input}}
            """;

        var result = await kernel.InvokePromptAsync(
            priorBlock + unifiedPrompt,
            new KernelArguments { ["input"] = userMessage },
            cancellationToken: cancellationToken);

        return result.GetValue<string>() ?? "";
    }

    private static void SplitRoutingAndPlan(string raw, out string routingBody, out string planBody)
    {
        var match = Regex.Match(raw, @"\r?\n\s*PLAN\s*\r?\n", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            routingBody = raw.Trim();
            planBody = "";
            return;
        }

        routingBody = raw[..match.Index].Trim();
        planBody = raw[(match.Index + match.Length)..].Trim();
    }

    private static AgentPlan ParsePlanSection(string planText)
    {
        if (string.IsNullOrWhiteSpace(planText))
            return new AgentPlan("Resolve travel disruption", []);

        var lines = planText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var goalLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("GOAL:", StringComparison.OrdinalIgnoreCase));
        var goal = goalLine?.Split(':', 2).LastOrDefault()?.Trim() ?? "Resolve travel disruption";

        var steps = lines
            .Where(l => Regex.IsMatch(l.TrimStart(), @"^\d+[\.\)]\s"))
            .Select(l => Regex.Replace(l.TrimStart(), @"^\d+[\.\)]\s*", "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return new AgentPlan(goal, steps);
    }

    private async Task<ConversationRouteResult> RouteWithLlmAsync(
        string userMessage,
        string? priorConversationContext,
        CancellationToken cancellationToken)
    {
        var kernel = _kernelFactory.CreateKernel();

        var priorBlock = string.IsNullOrWhiteSpace(priorConversationContext)
            ? ""
            : $"""

            Prior conversation (earlier turns only; the latest user message is given separately below):
            {priorConversationContext.Trim()}

            """;

        const string routingPrompt = """
            You route user messages for a Travel Disruption Assistant. Output a single routing decision.

            IN_SCOPE includes:
            - Live flight status, delays, cancellations, diversions, gates, connections, rebooking, weather at airports
            - Travel disruption assistance (what to do if stranded, delayed, cancelled)
            - Company **business travel** policy: meals, hotels, expenses, reimbursement, what employer covers,
              rebooking rules, baggage/check-in rules, meeting buffers, compensation summaries (EU261-style facts as policy text)

            OUT_OF_SCOPE: jokes, coding, sports, trivia, unrelated privacy/consumer policies, non-travel topics.

            Respond in EXACTLY this format (one line each):
            SCOPE: IN_SCOPE or OUT_OF_SCOPE
            INTENT: flight_operational | weather_operational | disruption_assistance | company_travel_policy | reimbursement_expenses | baggage_checkin_policy | rebooking_policy | missed_connection | strike_disruption | flight_diversion | mixed_disruption | out_of_scope
            NEEDS_TOOLS: yes or no
            NEEDS_RAG: yes or no
            INTERNAL_ONLY: yes or no
            RAG_TOPICS: comma-separated short labels (e.g. expense-limits,compensation-rights) or none
            REASON: one concise English sentence

            Rules:
            - NEEDS_TOOLS yes if flight lookup by number is needed, or route/weather APIs are needed because the user explicitly asked for alternatives/rebooking/route options or weather/forecast. Do not set yes for route/weather on status-only questions.
            - NEEDS_RAG yes if company policy / reimbursement / rights / procedural guidance from policy docs is needed (including questions that combine live data + policy).
            - mixed_disruption when both tools and policy clearly apply.
            - INTERNAL_ONLY yes only for vague in-domain tips with no tools and no policy retrieval.

            User message: {{$input}}
            """;

        var result = await kernel.InvokePromptAsync(
            priorBlock + routingPrompt,
            new KernelArguments { ["input"] = userMessage },
            cancellationToken: cancellationToken);

        return ParseLlmRoute(result.GetValue<string>() ?? "", userMessage);
    }

    private static ConversationRouteResult ParseLlmRoute(string response, string userMessage)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string? line(string prefix) =>
            lines.FirstOrDefault(l => l.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        bool inScope = line("SCOPE:")?.Contains("IN_SCOPE", StringComparison.OrdinalIgnoreCase) ?? false;
        var intentRaw = line("INTENT:")?.Split(':', 2).LastOrDefault()?.Trim() ?? AgentIntents.OutOfScope;
        var intent = NormalizeIntent(intentRaw);
        bool needsTools = ParseYes(line("NEEDS_TOOLS:"));
        bool needsRag = ParseYes(line("NEEDS_RAG:"));
        bool internalOnly = ParseYes(line("INTERNAL_ONLY:"));
        var reason = line("REASON:")?.Split(':', 2).LastOrDefault()?.Trim() ?? "LLM routing";
        var topics = ParseRagTopics(line("RAG_TOPICS:"));

        if (!inScope)
            return new ConversationRouteResult(
                false, OutOfScopeMessage, AgentIntents.OutOfScope,
                false, false, false, reason, [], null);

        return new ConversationRouteResult(
            true, null, intent, needsTools, needsRag, internalOnly, reason, topics, null);
    }

    private static bool ParseYes(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var v = line.Split(':', 2).LastOrDefault()?.Trim() ?? "";
        return v.StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseRagTopics(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        var v = line.Split(':', 2).LastOrDefault()?.Trim() ?? "";
        if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        return v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static string NormalizeIntent(string raw)
    {
        var key = raw.ToLowerInvariant().Replace(' ', '_');
        return key switch
        {
            "flight_operational" => AgentIntents.FlightOperational,
            "weather_operational" => AgentIntents.WeatherOperational,
            "disruption_assistance" => AgentIntents.DisruptionAssistance,
            "company_travel_policy" => AgentIntents.CompanyTravelPolicy,
            "reimbursement_expenses" => AgentIntents.ReimbursementExpenses,
            "baggage_checkin_policy" => AgentIntents.BaggageCheckinPolicy,
            "rebooking_policy" => AgentIntents.RebookingPolicy,
            "missed_connection" => AgentIntents.MissedConnection,
            "strike_disruption" => AgentIntents.StrikeDisruption,
            "flight_diversion" => AgentIntents.FlightDiversion,
            "mixed_disruption" => AgentIntents.MixedDisruption,
            "out_of_scope" => AgentIntents.OutOfScope,
            // Legacy LLM labels — keep as workflow-compatible tokens; ToWorkflowIntent passes them through.
            "flight_cancellation" => "flight_cancellation",
            "flight_delay" => "flight_delay",
            "weather_disruption" => "weather_disruption",
            "rebooking_request" => "rebooking_request",
            "baggage_issue" => "baggage_issue",
            "general_travel_disruption" => "general_travel_disruption",
            _ => AgentIntents.DisruptionAssistance
        };
    }

    private async Task<ConversationRouteResult> RefineWithSignalsAsync(
        string userMessage,
        ConversationRouteResult llm,
        string? llmFallback,
        CancellationToken ct)
    {
        if (!llm.InScope)
        {
            var recover = await _ragService.GetPolicyRagHintsAsync(userMessage, ct);
            if (recover.SuggestsPolicy && recover.BestSimilarity >= 0.52)
            {
                _logger.LogDebug("Recovered IN_SCOPE from strong policy embedding despite LLM OUT_OF_SCOPE");
                return new ConversationRouteResult(
                    true, null,
                    AgentIntents.CompanyTravelPolicy,
                    HasFlightNumber(userMessage) || HasIataPair(userMessage),
                    true,
                    false,
                    "Strong semantic match to company travel policy knowledge base",
                    recover.SuggestedRagTopics.ToList(),
                    llmFallback);
            }

            return llm with { LlmFallbackReason = llmFallback };
        }

        var hints = await _ragService.GetPolicyRagHintsAsync(userMessage, ct);
        var needsTools = llm.NeedsTools || HasFlightNumber(userMessage) || HasIataPair(userMessage) ||
                         LooksLikeWeatherQuery(userMessage);
        var needsRag = llm.NeedsRag || hints.SuggestsPolicy ||
                       IntentCatalog.GranularIntentImpliesRag(llm.Intent) ||
                       (needsTools && hints.SuggestsPolicy) ||
                       llm.Intent == AgentIntents.WeatherOperational;

        var intent = llm.Intent;
        if (needsTools && needsRag && intent != AgentIntents.MixedDisruption)
            intent = AgentIntents.MixedDisruption;

        var topics = llm.SuggestedRagTopics.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var t in hints.SuggestedRagTopics)
            topics.Add(t);
        if (needsRag && topics.Count == 0)
            topics.Add("general-policy");

        var internalOnly = llm.UseInternalOnly && !needsTools && !needsRag;

        var reason = llm.Reason;
        if (hints.SuggestsPolicy)
            reason += $" (policy embedding {hints.BestSimilarity:F2})";

        return new ConversationRouteResult(
            true, null, intent, needsTools, needsRag, internalOnly,
            reason, topics.ToList(), llmFallback);
    }

    private async Task<ConversationRouteResult> RouteWithSignalsAsync(
        string userMessage, CancellationToken cancellationToken)
    {
        if (ObviousOutOfScope(userMessage))
            return Out(AgentIntents.OutOfScope, "Heuristic: not travel or company travel policy");

        var hints = await _ragService.GetPolicyRagHintsAsync(userMessage, cancellationToken);
        if (hints.SuggestsPolicy)
        {
            var intent = InferPolicyIntent(userMessage);
            var needsTools = HasFlightNumber(userMessage) || HasIataPair(userMessage) ||
                             LooksLikeWeatherQuery(userMessage);
            var needsRag = true;
            if (needsTools && needsRag)
                intent = AgentIntents.MixedDisruption;
            var topics = hints.SuggestedRagTopics.ToList();
            return new ConversationRouteResult(
                true, null, intent, needsTools, needsRag, false,
                "Semantic match to company travel policy corpus", topics, null);
        }

        if (HasFlightNumber(userMessage) || HasIataPair(userMessage))
        {
            var intent = ContainsDisruptionLanguage(userMessage)
                ? AgentIntents.MixedDisruption
                : AgentIntents.FlightOperational;
            return new ConversationRouteResult(
                true, null, intent, true, intent == AgentIntents.MixedDisruption || hints.SuggestsPolicy,
                false, "Flight or route identifiers in message", ["general-policy"], null);
        }

        if (LooksLikeWeatherQuery(userMessage))
            return new ConversationRouteResult(
                true, null, AgentIntents.WeatherOperational, true,
                true,
                false,
                "Weather-related query — include disruption policy context",
                hints.SuggestedRagTopics.Count > 0 ? hints.SuggestedRagTopics.ToList() : ["general-policy"],
                null);

        if (ContainsDisruptionLanguage(userMessage))
            return new ConversationRouteResult(
                true, null,
                AgentIntents.DisruptionAssistance,
                true,
                true,
                false,
                "Disruption-related language detected",
                hints.SuggestedRagTopics.Count > 0 ? hints.SuggestedRagTopics.ToList() : ["general-policy"],
                null);

        return Out(AgentIntents.OutOfScope, "No travel, disruption, or policy signal");
    }

    private static string InferPolicyIntent(string m)
    {
        var l = m.ToLowerInvariant();
        if (l.Contains("baggage") || l.Contains("luggage") || l.Contains("check-in") || l.Contains("checkin"))
            return AgentIntents.BaggageCheckinPolicy;
        if (l.Contains("rebook") || l.Contains("rebooking"))
            return AgentIntents.RebookingPolicy;
        if (l.Contains("meal") || l.Contains("food") || l.Contains("hotel") || l.Contains("expense") ||
            l.Contains("reimburs") || l.Contains("claim") || l.Contains("voucher") || l.Contains("cover"))
            return AgentIntents.ReimbursementExpenses;
        return AgentIntents.CompanyTravelPolicy;
    }

    private static bool ObviousOutOfScope(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("tell me a joke") || m.Contains("write python") || m.Contains("meaning of life"))
            return true;
        if (m.Contains("who won") && (m.Contains("game") || m.Contains("match")))
            return true;
        return false;
    }

    private static bool HasFlightNumber(string message) =>
        Regex.IsMatch(message, @"\b[A-Z]{2}\s?\d{1,4}\b", RegexOptions.IgnoreCase);

    private static bool HasIataPair(string message)
    {
        var codes = Regex.Matches(message.ToUpperInvariant(), @"\b[A-Z]{3}\b")
            .Select(x => x.Value)
            .Where(c => c is not "THE" and not "AND" and not "FOR")
            .Distinct()
            .Take(4)
            .ToList();
        return codes.Count >= 2;
    }

    private static bool LooksLikeWeatherQuery(string m)
    {
        var l = m.ToLowerInvariant();
        return (l.Contains("weather") || l.Contains("forecast") || l.Contains("storm") || l.Contains("snow")) &&
               (l.Contains("airport") || Regex.IsMatch(m, @"\b[A-Z]{3}\b") || l.Contains(" at "));
    }

    private static bool ContainsDisruptionLanguage(string m)
    {
        var l = m.ToLowerInvariant();
        return l.Contains("cancel") || l.Contains("delay") || l.Contains("disrupt") ||
               l.Contains("stranded") || l.Contains("missed connection") || l.Contains("divert") ||
               l.Contains("grounded") || l.Contains("strike") || l.Contains("rebook");
    }

    private static ConversationRouteResult Out(string intent, string reason) =>
        new(false, OutOfScopeMessage, intent, false, false, false, reason, [], null);

}
