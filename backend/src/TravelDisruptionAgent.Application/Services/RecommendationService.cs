using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Utilities;
using TravelDisruptionAgent.Domain.Entities;
using Microsoft.SemanticKernel;

namespace TravelDisruptionAgent.Application.Services;

public class RecommendationService : IRecommendationService
{
    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<RecommendationService> _logger;

    public RecommendationService(
        IKernelFactory kernelFactory,
        ILogger<RecommendationService> logger)
    {
        _kernelFactory = kernelFactory;
        _logger = logger;
    }

    public async Task<RecommendationResult> GenerateAsync(
        string userMessage, string intent, List<ToolExecutionResult> toolResults,
        UserPreferences? preferences = null,
        IReadOnlyList<string>? ragContext = null,
        PlanValidationResult? validation = null,
        DateTime? requestTime = null,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = VerifiedContextBuilder.Build(intent, toolResults, ragContext, validation, requestTime);

        if (_kernelFactory.IsConfigured)
        {
            try
            {
                return await GenerateWithLlmAsync(
                    userMessage, ctx, toolResults, preferences, priorConversationContext, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM recommendation generation failed");
                var errorDetail = LlmErrorDescriptions.GetPublicMessage(ex);
                return new RecommendationResult(
                    "## AI summary unavailable\n\n" +
                    "The **final AI wording step** failed. This is **not** the same as \"no flights were found\" — " +
                    "flight lookup, route search, and weather calls already finished before this step.\n\n" +
                    $"**What went wrong:** {errorDetail}\n\n" +
                    "**What still works:** Open **Flight Data** and **Weather Conditions** in the panel below for the live tool results.\n\n" +
                    "A template-based fallback paragraph was not inserted here on purpose, so you can see the real failure reason. " +
                    "Retry in a moment if you hit a rate limit, or check API quota and billing in Google AI Studio / Cloud Console.",
                    0.0,
                    [$"LLM unavailable: {errorDetail}"],
                    LlmFailed: true,
                    LlmErrorDetail: errorDetail);
            }
        }

        return GenerateTemplateBased(ctx, toolResults, preferences);
    }

    // ── LLM path ─────────────────────────────────────────────────────

    private async Task<RecommendationResult> GenerateWithLlmAsync(
        string userMessage,
        VerifiedContext ctx,
        List<ToolExecutionResult> toolResults,
        UserPreferences? preferences,
        string? priorConversationContext,
        CancellationToken cancellationToken)
    {
        var kernel = _kernelFactory.CreateKernel();

        var structuredContext = VerifiedContextBuilder.SerializeForPrompt(ctx);

        var prefsContext = preferences is not null
            ? $"User preferences: Risk tolerance={preferences.RiskTolerance}, " +
              $"Preferred airline={preferences.PreferredAirline}, " +
              $"Meeting buffer={preferences.PreferredMeetingBufferMinutes}min, " +
              $"Prefers remote fallback={preferences.PrefersRemoteFallback}, " +
              $"Home airport={preferences.HomeAirport}"
            : "No user preferences available.";

        var priorBlock = string.IsNullOrWhiteSpace(priorConversationContext)
            ? ""
            : $"""

            Prior conversation (earlier turns only):
            {priorConversationContext.Trim()}

            """;

        const string recommendationPrompt = """
            You are a Travel Disruption Agent. Generate a recommendation using ONLY the structured
            context below. Do NOT invent, assume, or infer any data not present.

            Write the entire response in English only, regardless of the user's message language.

            YOUR RESPONSE MUST HAVE EXACTLY 4 SECTIONS in this order:

            ## Verified Facts (from live data)
            Summarise facts from the VERIFIED FACTS section only.
            - For the passenger's own flight, use ONLY lines marked [PRIMARY — FlightLookupByNumber].
              Ignore any other line with the same flight number (e.g. duplicate rows from route search).
            - Use phrases like "According to live data returned…", "Flight data shows…"
            - If actual departure/arrival times exist, the flight data IS available — never
              write "real-time flight data was unavailable" when actual times are present.
            - If a flight "has departed / en route" or "has departed and arrived", its status
              IS known — do NOT say "we could not verify the status". Instead say the flight
              departed at the stated time.
            - If only scheduled times exist, say "Only scheduled times are available; actual
              times have not been confirmed."

            ## Relevant Policy
            Summarise applicable policies from the POLICY CONTEXT section.
            - Use "According to airline/company policy…"
            - If no policies were retrieved, write "No relevant policy documents were found."

            ## What Is Still Unknown
            List items from the UNVERIFIED / UNKNOWN section ONLY.
            - Do NOT add items that are already covered by verified facts.
            - If a flight has actual departure times and gate info, its status is effectively
              known — do NOT list it as unknown.
            - Use "We could not verify…", "The cause of the disruption has not been confirmed…"
            - If compensation depends on the cancellation reason AND the reason is unverified,
              state this explicitly.
            - If UNVERIFIED / UNKNOWN section is "(none)" or empty, write "All key facts have
              been verified."

            ## Recommended Actions
            Provide concrete next steps the passenger should take.
            - Do not mention alternative flights, rebooking from a route search, or current weather at airports unless
              (1) the user explicitly asked for alternatives/rebooking/weather, or (2) EXECUTED TOOLS includes
              FlightLookupByRoute or WeatherLookup with successful data you must cite from context.
            - If the section ALTERNATIVE FLIGHTS AVAILABLE exists and lists flights, present
              ONLY those alternatives (codeshare-deduped; default window is request+1h through +24h,
              widened stepwise to 48h, 72h, 96h, 120h only if nothing was found in a smaller window).
              If the context includes a "Window note" about expansion past 24h, state clearly that
              nothing was found in the first 24 hours and the list reflects the widened search.
              Do NOT mention the user's own flight as an alternative.
              Every FlightLookupByNumber **Input** in EXECUTED TOOLS is that passenger's own flight
              number — never list those exact identifiers as rebooking options, even if a duplicate
              row appears under route search or alternatives.
              List each alternative with flight number, airline, departure time, and gate.
              Keep it to the top 3-4 most useful options (soonest departures first).
            - If ALTERNATIVE FLIGHTS says none matched after widening up to 120h (5 days), say that
              no alternatives were found from 1 hour after the request through 5 days forward in the
              data retrieved, and suggest contacting the airline. Do NOT claim only "24 hours" if
              the context describes a wider search.
            - If FlightLookupByRoute does NOT appear at all in EXECUTED TOOLS, do NOT say
              "no alternatives were found" (because we never searched). Instead either skip
              mentioning alternatives, or say "alternative flights were not searched" if the
              user asked about options.
            - Only attribute the disruption to weather if weather risk is MODERATE or higher.
            - Tailor advice to user preferences if available.
            - NEVER include flights from previous dates as recommendations.
            - NEVER say "no alternatives found" or "no same-day alternatives" unless
              FlightLookupByRoute was actually executed and returned no results.

            STRICT RULES:
            - NEVER reference actions or data that do not appear in the structured context.
            - NEVER fabricate flight numbers, times, or alternatives.
            - If a fact is in UNVERIFIED, do NOT present it as confirmed.
            - If data is from a fallback/simulated source, note it.

            """;

        const string recommendationPromptTail = """

            User's situation: {{$userMessage}}
            {{$prefsContext}}

            {{$structuredContext}}
            """;

        var result = await kernel.InvokePromptAsync(
            recommendationPrompt + priorBlock + recommendationPromptTail,
            new KernelArguments
            {
                ["userMessage"] = userMessage,
                ["prefsContext"] = prefsContext,
                ["structuredContext"] = structuredContext
            },
            cancellationToken: cancellationToken);

        var recommendation = result.GetValue<string>() ?? "Unable to generate recommendation.";
        var confidence = ComputeConfidence(ctx, toolResults, preferences);

        var warnings = toolResults
            .Where(t => t.Warning is not null)
            .Select(t => t.Warning!)
            .ToList();
        foreach (var c in ctx.Contradictions)
            warnings.Add(c);

        return new RecommendationResult(recommendation, confidence, warnings);
    }

    // ── Template path (no LLM) ───────────────────────────────────────

    private RecommendationResult GenerateTemplateBased(
        VerifiedContext ctx,
        List<ToolExecutionResult> toolResults,
        UserPreferences? preferences)
    {
        _logger.LogDebug("Generating template-based recommendation for intent: {Intent}", ctx.Intent);

        var sb = new StringBuilder();
        sb.AppendLine($"## Travel Disruption Analysis: {FormatIntent(ctx.Intent)}");
        sb.AppendLine();

        if (preferences is not null && !string.IsNullOrEmpty(preferences.PreferredAirline))
        {
            sb.AppendLine($"*Personalized for your preferences (risk: {preferences.RiskTolerance}, preferred airline: {preferences.PreferredAirline})*");
            sb.AppendLine();
        }

        // Section 1: Verified facts
        sb.AppendLine("### Verified Facts (from live data)");
        if (ctx.VerifiedFacts.Count > 0)
        {
            foreach (var fact in ctx.VerifiedFacts)
                sb.AppendLine($"- {fact}");
        }
        else
        {
            sb.AppendLine("- No verified data was retrieved from live sources.");
        }
        sb.AppendLine();

        // Section 2: Relevant policy
        sb.AppendLine("### Relevant Policy");
        if (ctx.PolicyContext.Count > 0)
        {
            foreach (var policy in ctx.PolicyContext)
            {
                var firstLine = policy.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim();
                if (firstLine is not null)
                    sb.AppendLine($"- {firstLine}");
            }
        }
        else
        {
            sb.AppendLine("- No relevant policy documents were found.");
        }
        sb.AppendLine();

        // Section 3: What is still unknown
        sb.AppendLine("### What Is Still Unknown");
        if (ctx.UnverifiedFacts.Count > 0)
        {
            foreach (var fact in ctx.UnverifiedFacts)
                sb.AppendLine($"- {fact}");
        }
        else
        {
            sb.AppendLine("- All key facts have been verified.");
        }
        sb.AppendLine();

        // Section 4: Recommended actions
        sb.AppendLine("### Recommended Actions");
        sb.AppendLine(GetIntentRecommendation(ctx, preferences));

        var confidence = ComputeConfidence(ctx, toolResults, preferences);

        var warnings = toolResults
            .Where(t => t.Warning is not null)
            .Select(t => t.Warning!)
            .ToList();
        foreach (var c in ctx.Contradictions)
            warnings.Add(c);

        return new RecommendationResult(sb.ToString(), confidence, warnings);
    }

    // ── Intent-specific recommendations (template) ────────────────────

    private static string GetIntentRecommendation(VerifiedContext ctx, UserPreferences? prefs)
    {
        var sb = new StringBuilder();

        var riskNote = prefs?.RiskTolerance switch
        {
            "safe" => "Based on your conservative risk preference, consider rebooking proactively rather than waiting.",
            "aggressive" => "Based on your flexible risk preference, you may choose to wait for updates before rebooking.",
            _ => (string?)null
        };

        var remoteNote = prefs?.PrefersRemoteFallback == true
            ? "Consider joining your meeting remotely if the disruption cannot be resolved in time."
            : null;

        var bufferNote = prefs?.PreferredMeetingBufferMinutes > 0
            ? $"You prefer {prefs.PreferredMeetingBufferMinutes} minutes between arrival and meetings."
            : null;

        switch (ctx.Intent)
        {
            case "flight_cancellation":
                sb.AppendLine("1. Contact your airline immediately for rebooking — most airlines offer free rebooking for cancellations.");
                if (ctx.HasAlternativeFlights)
                    sb.AppendLine("2. Review the alternative flights found above for availability.");
                else
                    sb.AppendLine("2. We were unable to retrieve alternative flights at this time — contact your airline directly for available options.");

                sb.AppendLine("3. If no same-day alternatives exist, request hotel accommodation from the airline.");

                if (!ctx.CancellationVerified)
                    sb.AppendLine("4. **Note:** The flight cancellation could not be confirmed through our data sources. Please verify the cancellation status directly with your airline.");
                else if (!ctx.CancellationReasonKnown)
                    sb.AppendLine("4. **Note:** The cancellation reason has not been confirmed. If compensation depends on the cause (e.g. weather vs. operational), verify with your airline before filing a claim.");
                else
                    sb.AppendLine("4. Keep all receipts for potential compensation claims (EU261 or DOT rules may apply).");

                if (ctx.WeatherIsCausal)
                    sb.AppendLine("5. Weather conditions may affect alternative flights as well — consider flexible rebooking.");
                else if (ctx.Weather.Count > 0 && !ctx.WeatherIsCausal)
                    sb.AppendLine("5. Weather conditions are currently not a major factor at the relevant airports.");
                break;

            case "flight_delay":
                if (ctx.HasLiveFlightData)
                    sb.AppendLine("Based on the flight data retrieved:");
                else
                    sb.AppendLine("Regarding your flight delay:");
                sb.AppendLine("1. Monitor the flight status for updates — delays can extend or resolve.");
                sb.AppendLine("2. If the delay exceeds 2 hours, you may be entitled to meal vouchers from the airline.");
                sb.AppendLine("3. If the delay exceeds 3+ hours, check your rights for compensation.");
                sb.AppendLine("4. If you have a connecting flight, contact the airline to protect your connection.");
                if (!ctx.WeatherIsCausal && ctx.Weather.Count > 0)
                    sb.AppendLine("5. Weather is not currently a significant disruption factor.");
                break;

            case "weather_disruption":
                if (ctx.WeatherIsCausal)
                    sb.AppendLine("1. Severe/moderate weather detected — expect potential disruptions. Consider rebooking proactively.");
                else if (ctx.Weather.Count > 0)
                    sb.AppendLine("1. Current weather conditions are minimal/low risk. The disruption may not be weather-related.");
                else
                    sb.AppendLine("1. Weather data could not be retrieved — unable to assess weather impact.");
                sb.AppendLine("2. Check your airline's weather waiver policy — many offer free changes during weather events.");
                sb.AppendLine("3. Consider travel insurance for weather-related expenses.");
                sb.AppendLine("4. Have a backup plan ready in case conditions worsen.");
                break;

            case "missed_connection":
                sb.AppendLine("1. Go to the nearest airline service desk immediately — they can rebook you on the next available flight.");
                sb.AppendLine("2. If the missed connection was due to an airline-caused delay, rebooking should be free.");
                sb.AppendLine("3. Ask about partner airline options if your airline has no immediate alternatives.");
                sb.AppendLine("4. Request meal/hotel vouchers if you need to wait for the next day.");
                break;

            case "rebooking_request":
                if (ctx.HasAlternativeFlights)
                {
                    sb.AppendLine("1. Review the alternative flights found above.");
                    sb.AppendLine("2. Contact your airline to request rebooking — reference the specific flights you prefer.");
                }
                else
                {
                    sb.AppendLine("1. We were unable to retrieve alternative flights at this time.");
                    sb.AppendLine("2. Contact your airline to request rebooking and ask about available options.");
                }
                sb.AppendLine("3. Check fare differences — disruption-related rebooking is often free.");
                sb.AppendLine("4. Consider nearby airports if direct alternatives are limited.");
                break;

            default:
                if (ctx.HasLiveFlightData)
                    sb.AppendLine("Based on the flight data retrieved:");
                sb.AppendLine("1. Document your situation and keep all travel documents.");
                if (ctx.HasAlternativeFlights)
                    sb.AppendLine("2. Review the alternative flights found above for rebooking options.");
                else
                    sb.AppendLine("2. Contact your airline for assistance and rebooking options.");
                sb.AppendLine("3. Check your travel insurance coverage.");
                sb.AppendLine("4. Monitor the situation for updates.");
                break;
        }

        if (riskNote is not null) sb.AppendLine($"\n**Preference:** {riskNote}");
        if (remoteNote is not null) sb.AppendLine($"**Remote option:** {remoteNote}");
        if (bufferNote is not null) sb.AppendLine($"**Meeting buffer:** {bufferNote}");

        return sb.ToString();
    }

    // ── Confidence calculation ────────────────────────────────────────

    private static double ComputeConfidence(
        VerifiedContext ctx,
        List<ToolExecutionResult> toolResults,
        UserPreferences? preferences)
    {
        var successRate = toolResults.Count > 0
            ? (double)toolResults.Count(t => t.Success) / toolResults.Count
            : 0.5;
        var confidence = 0.5 + (successRate * 0.4);
        if (ctx.PolicyContext.Count > 0) confidence += 0.05;
        if (preferences is not null) confidence += 0.03;
        if (ctx.HasActualTimes) confidence += 0.05;
        confidence -= 0.05 * ctx.UnverifiedFacts.Count;
        confidence -= 0.05 * ctx.SkippedSteps.Count;
        return Math.Clamp(confidence, 0.1, 0.98);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string FormatIntent(string intent) =>
        intent.Replace('_', ' ').ToUpperInvariant();
}
