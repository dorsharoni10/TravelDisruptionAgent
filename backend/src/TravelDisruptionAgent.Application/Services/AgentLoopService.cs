using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Telemetry;
using TravelDisruptionAgent.Application.Utilities;
using TravelDisruptionAgent.Domain.Entities;
using TravelDisruptionAgent.Domain.Enums;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>LLM-driven agent loop with structured steps, grounding enforcement, citation checks, and transcript bounds.</summary>
public class AgentLoopService : IAgentLoopService
{
    private readonly IAgentLoopStepLlmInvoker _llmInvoker;
    private readonly IAgenticToolExecutor _toolExecutor;
    private readonly AgenticOptions _options;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<AgentLoopService> _logger;

    public AgentLoopService(
        IAgentLoopStepLlmInvoker llmInvoker,
        IAgenticToolExecutor toolExecutor,
        IOptions<AgenticOptions> options,
        IOptions<RagOptions> ragOptions,
        ILogger<AgentLoopService> logger)
    {
        _llmInvoker = llmInvoker;
        _toolExecutor = toolExecutor;
        _options = options.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    public async Task<AgentLoopRunResult> RunAsync(
        string userMessage,
        ConversationRouteResult route,
        string workflowIntent,
        IReadOnlyList<string> planSteps,
        UserPreferences? preferences,
        Func<AgentStepType, string, string, Task>? emit,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!_llmInvoker.IsConfigured)
        {
            return new AgentLoopRunResult
            {
                Succeeded = false,
                StopReason = AgenticStopReason.Disabled,
                Errors = ["LLM kernel not configured"]
            };
        }

        using var loopActivity = AgenticTelemetry.Activity.StartActivity("agentic.loop");
        loopActivity?.SetTag("route.intent", route.Intent);
        loopActivity?.SetTag("route.needs_rag", route.NeedsRag);
        loopActivity?.SetTag("route.needs_tools", route.NeedsTools);

        var toolResults = new List<ToolExecutionResult>();
        var policyChunkTexts = new List<string>();
        var policyQueries = new List<string>();
        var trace = new List<AgentLoopIterationRecord>();
        var errors = new List<string>();
        var policyQueryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var retrievedPolicyDocIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestUtc = DateTime.UtcNow;
        var ctx = new AgenticToolContext(userMessage, requestUtc);

        var transcriptBuffer = new AgentTranscriptBuffer(
            _options.TranscriptRetentionObservations,
            _options.TranscriptMaxChars);

        string? lastObservation = null;
        var stagnation = 0;
        var parseFailures = 0;
        var iteration = 0;
        var usedStructuredLlm = false;
        var usedLegacyLlm = false;

        var prefLine = FormatPreferences(preferences);
        var planText = planSteps.Count > 0
            ? string.Join("\n", planSteps.Select((s, i) => $"{i + 1}. {s}"))
            : "(no plan steps)";

        var systemInstructions = BuildSystemInstructions(workflowIntent);

        while (iteration < _options.MaxIterations)
        {
            iteration++;
            using var iterActivity = AgenticTelemetry.Activity.StartActivity("agentic.iteration");
            iterActivity?.SetTag("iteration.index", iteration);

            var userTurn = BuildUserTurn(
                userMessage, route, workflowIntent, planText, prefLine, transcriptBuffer, priorConversationContext);
            AgentLlmInvocationResult llmResult;
            try
            {
                llmResult = await _llmInvoker.InvokeStructuredStepAsync(
                    systemInstructions,
                    userTurn,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agentic loop LLM invocation failed at iteration {Iter}", iteration);
                errors.Add($"llm_error: {ex.Message}");
                return Finish(FinalResult(false, AgenticStopReason.ModelError, null, 0, toolResults, policyChunkTexts,
                    trace, errors, policyQueries, iteration, policyQueries.Count, usedStructuredLlm, usedLegacyLlm,
                    false, []));
            }

            if (llmResult.Kind == AgentLlmInvocationKind.StructuredSchema && llmResult.Succeeded)
                usedStructuredLlm = true;
            if (llmResult.Kind == AgentLlmInvocationKind.LegacyTextJson)
                usedLegacyLlm = true;

            if (!llmResult.Succeeded || llmResult.Output is null)
            {
                parseFailures++;
                errors.Add($"llm_step_failed_iter{iteration}: {llmResult.Error ?? "unknown"}");
                transcriptBuffer.AppendObservation(iteration,
                    $"[system] LLM step invalid. Snippet: {Truncate(llmResult.RawSnippet ?? "", 300)}");
                if (parseFailures >= _options.MaxConsecutiveParseFailures)
                    return Finish(FinalResult(false, AgenticStopReason.ParseFailures, null, 0, toolResults,
                        policyChunkTexts, trace, errors, policyQueries, iteration, policyQueries.Count,
                        usedStructuredLlm, usedLegacyLlm, false, []));
                continue;
            }

            var step = llmResult.Output;
            parseFailures = 0;
            var args = ParseArgumentsJson(step.ArgumentsJson);
            var argsSummary = args.Count > 0
                ? string.Join(", ", args.Select(p => $"{p.Key}={Truncate(p.Value ?? "", 80)}"))
                : "(none)";

            var action = step.Action.Trim().ToLowerInvariant().Replace(' ', '_');
            if (action is "final_answer" or "answer" or "complete")
            {
                var answer = step.FinalAnswer?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(answer))
                {
                    transcriptBuffer.AppendObservation(iteration,
                        "[system] final_answer was empty — provide non-empty final_answer or invoke_tool.");
                    trace.Add(new AgentLoopIterationRecord
                    {
                        Index = iteration,
                        Thought = step.Thought,
                        KnownSummary = step.KnownSummary,
                        StillMissing = step.StillMissing,
                        Action = action,
                        Capability = "",
                        ArgumentsSummary = argsSummary,
                        ObservationSummary = "(empty final answer — continue)"
                    });
                    continue;
                }

                var groundingFallback = false;
                if (route.NeedsRag && retrievedPolicyDocIds.Count == 0)
                {
                    var allowWithoutPolicy = ShouldAllowOperationalAnswerWithoutPolicy(
                        userMessage, priorConversationContext, toolResults);

                    if (!allowWithoutPolicy)
                    {
                        answer = policyQueries.Count > 0
                            ? _options.PolicyInsufficientKbMessage
                            : _options.PolicyNoRetrievalMessage;
                        groundingFallback = true;
                        errors.Add(policyQueries.Count > 0
                            ? "grounding:policy_kb_insufficient_or_missing"
                            : "grounding:no_policy_retrieval");
                    }
                    else
                    {
                        errors.Add(
                            "grounding:skipped_policy_requirement_for_operational_follow_up_using_tools_or_chat_history");
                    }
                }

                var (cleanAnswer, citeWarnings) =
                    AgentCitationValidator.SanitizeCitations(answer, retrievedPolicyDocIds);

                var groundedPolicy = retrievedPolicyDocIds.Count > 0;
                var groundedTools = toolResults.Any(t =>
                    t.Success && t.ToolName is not "search_policy_knowledge" and not "search_internal_knowledge"
                        and not "Agent");

                trace.Add(new AgentLoopIterationRecord
                {
                    Index = iteration,
                    Thought = step.Thought,
                    KnownSummary = step.KnownSummary,
                    StillMissing = step.StillMissing,
                    Action = "final_answer",
                    Capability = "",
                    ArgumentsSummary = argsSummary,
                    ObservationSummary =
                        $"confidence={step.Confidence:F2}, sufficient_evidence={step.SufficientEvidence}, grounding_fallback={groundingFallback}"
                });

                if (emit is not null)
                    await emit(AgentStepType.AgentReasoning, $"Agent step {iteration} — final",
                        $"{step.KnownSummary}\n→ {Truncate(cleanAnswer, 500)}").ConfigureAwait(false);

                _logger.LogInformation(
                    "Agentic loop completed: iterations={Iter}, policyRetrievals={Pr}, tools={Tools}, groundedPolicy={Gp}, groundingFallback={Gf}, structured={Struct}, legacy={Leg}",
                    iteration, policyQueries.Count, toolResults.Count, groundedPolicy, groundingFallback,
                    usedStructuredLlm, usedLegacyLlm);

                return Finish(new AgentLoopRunResult
                {
                    Succeeded = true,
                    StopReason = groundingFallback ? AgenticStopReason.GroundingFallback : AgenticStopReason.FinalAnswer,
                    FinalAnswer = cleanAnswer,
                    Confidence = Clamp01(step.Confidence),
                    ToolResults = toolResults,
                    PolicyChunks = policyChunkTexts.Distinct().ToList(),
                    Trace = trace,
                    Errors = errors,
                    PolicyRetrievalCount = policyQueries.Count,
                    PolicyQueriesExecuted = policyQueries.ToList(),
                    IterationCount = iteration,
                    AnswerGroundedOnPolicy = groundedPolicy,
                    AnswerGroundedOnTools = groundedTools,
                    UsedStructuredLlmOutput = usedStructuredLlm && !usedLegacyLlm,
                    PolicyGroundingFallbackApplied = groundingFallback,
                    CitationSanitizationWarnings = citeWarnings
                });
            }

            if (action is not "invoke_tool" and not "tool" and not "call_tool")
            {
                transcriptBuffer.AppendObservation(iteration,
                    $"[system] Unknown action \"{step.Action}\". Use invoke_tool or final_answer.");
                trace.Add(new AgentLoopIterationRecord
                {
                    Index = iteration,
                    Thought = step.Thought,
                    KnownSummary = step.KnownSummary,
                    StillMissing = step.StillMissing,
                    Action = step.Action,
                    Capability = step.Capability,
                    ArgumentsSummary = argsSummary,
                    ObservationSummary = "unknown action"
                });
                continue;
            }

            var cap = step.Capability?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(cap))
            {
                transcriptBuffer.AppendObservation(iteration, "[system] invoke_tool requires \"capability\".");
                continue;
            }

            AgenticToolInvocationResult inv;
            using (var toolAct = AgenticTelemetry.Activity.StartActivity("agentic.tool"))
            {
                toolAct?.SetTag("tool.capability", cap);
                try
                {
                    inv = await _toolExecutor.InvokeAsync(cap, args, ctx, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Agentic tool {Cap} failed", cap);
                    inv = new AgenticToolInvocationResult
                    {
                        Observation = $"Tool error: {ex.Message}",
                        ToolRecords =
                        [
                            new ToolExecutionResult(cap, argsSummary, ex.Message, false, "Agent", 0, ex.Message)
                        ]
                    };
                }

                toolAct?.SetTag("tool.policy_chunks", inv.PolicyChunkCount);
                toolAct?.SetTag("tool.best_similarity", inv.BestSimilarity);
            }

            if (inv.WasPolicyRetrieval && inv.BestSimilarity is { } sim)
                AgenticTelemetry.RetrievalBestSimilarity.Record(sim);

            toolResults.AddRange(inv.ToolRecords);
            if (inv.PolicyChunkTexts.Count > 0)
                policyChunkTexts.AddRange(inv.PolicyChunkTexts);
            foreach (var c in inv.PolicyRetrievalChunks)
            {
                if (c.SimilarityScore is null || c.SimilarityScore >= _ragOptions.DocumentRetrievalMinSimilarity)
                    retrievedPolicyDocIds.Add(c.DocumentId);
            }

            if (inv.WasPolicyRetrieval && inv.NormalizedPolicyQuery is { Length: > 0 } nq)
            {
                var qLogged = args.TryGetValue("query", out var qq) && !string.IsNullOrWhiteSpace(qq) ? qq.Trim() : nq;
                policyQueries.Add(qLogged);
                policyQueryCounts.TryGetValue(nq, out var c);
                policyQueryCounts[nq] = c + 1;
                if (policyQueryCounts[nq] >= _options.MaxIdenticalPolicyQueries)
                {
                    trace.Add(new AgentLoopIterationRecord
                    {
                        Index = iteration,
                        Thought = step.Thought,
                        KnownSummary = step.KnownSummary,
                        StillMissing = step.StillMissing,
                        Action = action,
                        Capability = cap,
                        ArgumentsSummary = argsSummary,
                        ObservationSummary = Truncate(inv.Observation, 400),
                        RetrievalBestSimilarity = inv.BestSimilarity,
                        RetrievalChunkCount = inv.PolicyChunkCount
                    });
                    errors.Add($"duplicate_policy_query: {nq}");
                    return Finish(FinalResult(false, AgenticStopReason.DuplicatePolicyQuery, null, 0, toolResults,
                        policyChunkTexts, trace, errors, policyQueries, iteration, policyQueries.Count,
                        usedStructuredLlm, usedLegacyLlm, false, []));
                }
            }

            var obsNorm = AgenticLoopHeuristics.NormalizeObservation(inv.Observation);
            if (lastObservation is not null &&
                AgenticLoopHeuristics.ObservationsContentEqual(lastObservation, obsNorm))
            {
                stagnation++;
                if (stagnation >= _options.MaxStagnationIterations)
                {
                    trace.Add(new AgentLoopIterationRecord
                    {
                        Index = iteration,
                        Thought = step.Thought,
                        KnownSummary = step.KnownSummary,
                        StillMissing = step.StillMissing,
                        Action = action,
                        Capability = cap,
                        ArgumentsSummary = argsSummary,
                        ObservationSummary = "(stagnation — same observation)",
                        RetrievalBestSimilarity = inv.BestSimilarity,
                        RetrievalChunkCount = inv.PolicyChunkCount
                    });
                    return Finish(FinalResult(false, AgenticStopReason.Stagnation, null, 0, toolResults,
                        policyChunkTexts, trace, errors, policyQueries, iteration, policyQueries.Count,
                        usedStructuredLlm, usedLegacyLlm, false, []));
                }
            }
            else
                stagnation = 0;

            lastObservation = obsNorm;
            transcriptBuffer.AppendObservation(iteration, inv.Observation);

            trace.Add(new AgentLoopIterationRecord
            {
                Index = iteration,
                Thought = step.Thought,
                KnownSummary = step.KnownSummary,
                StillMissing = step.StillMissing,
                Action = action,
                Capability = cap,
                ArgumentsSummary = argsSummary,
                ObservationSummary = Truncate(inv.Observation, 500),
                RetrievalBestSimilarity = inv.BestSimilarity,
                RetrievalChunkCount = inv.PolicyChunkCount
            });

            if (emit is not null)
            {
                var emitBody = $"{step.Thought}\n→ {cap}({argsSummary})\n{Truncate(inv.Observation, 1200)}";
                await emit(AgentStepType.AgentReasoning, $"Agent step {iteration}", emitBody).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Agentic iter {Iter}: capability={Cap}, policyChunks={Pc}, bestSim={Sim}, toolRecords+={Tr}",
                iteration, cap, inv.PolicyChunkCount, inv.BestSimilarity, inv.ToolRecords.Count);
        }

        return Finish(FinalResult(false, AgenticStopReason.MaxIterations, null, 0, toolResults, policyChunkTexts,
            trace, errors, policyQueries, iteration, policyQueries.Count, usedStructuredLlm, usedLegacyLlm, false, []));
    }

    private AgentLoopRunResult Finish(AgentLoopRunResult r)
    {
        AgenticTelemetry.LoopsCompleted.Add(1);
        AgenticTelemetry.LoopIterations.Record(r.IterationCount);
        AgenticTelemetry.PolicyRetrievals.Record(r.PolicyRetrievalCount);
        AgenticTelemetry.StopReason.Add(1, new KeyValuePair<string, object?>("reason", r.StopReason.ToString()));
        if (r.Succeeded)
            AgenticTelemetry.LoopsSucceeded.Add(1);
        else
            AgenticTelemetry.LoopsFailed.Add(1);

        return r;
    }

    private static AgentLoopRunResult FinalResult(
        bool success,
        AgenticStopReason stop,
        string? final,
        double confidence,
        List<ToolExecutionResult> tools,
        List<string> policyChunks,
        List<AgentLoopIterationRecord> trace,
        List<string> errors,
        List<string> policyQueries,
        int iterations,
        int policyCount,
        bool usedStructured,
        bool usedLegacy,
        bool groundingFallback,
        List<string> citeWarnings)
    {
        var groundedPolicy = policyChunks.Count > 0;
        var groundedTools = tools.Any(t =>
            t.Success && t.ToolName is not "search_policy_knowledge" and not "search_internal_knowledge");
        return new AgentLoopRunResult
        {
            Succeeded = success,
            StopReason = stop,
            FinalAnswer = final,
            Confidence = confidence,
            ToolResults = tools,
            PolicyChunks = policyChunks.Distinct().ToList(),
            Trace = trace,
            Errors = errors,
            PolicyRetrievalCount = policyCount,
            PolicyQueriesExecuted = policyQueries.ToList(),
            IterationCount = iterations,
            AnswerGroundedOnPolicy = success && groundedPolicy,
            AnswerGroundedOnTools = success && groundedTools,
            UsedStructuredLlmOutput = usedStructured && !usedLegacy,
            PolicyGroundingFallbackApplied = groundingFallback,
            CitationSanitizationWarnings = citeWarnings
        };
    }

    private static string BuildSystemInstructions(string workflowIntent) =>
        $"""
        You are the reasoning core of a Travel Disruption Agent. Each turn you either call exactly one tool (invoke_tool) or finish (final_answer).

        Language: Write all user-facing text (especially final_answer) in English only, even if the user wrote in another language.

        Grounding:
        - Do not invent company policy. Cite retrieved policy using bracket ids when present in evidence, e.g. [policy-expenses].
        - Live flight/weather facts must come only from tool observations.
        - Prior conversation may repeat flight times, routes, or status you already gave the user — use it for short follow-ups (e.g. "when is arrival?") together with new tool data when needed. Do not add new policy claims unless you retrieved policy in this run.

        Workflow hint: {workflowIntent}

        Capabilities (invoke_tool); pass arguments via arguments_json as a JSON string:
        1) search_policy_knowledge — query (required), top_k optional
        2) search_internal_knowledge — same as (1)
        3) search_flight_context — optional flight_number, origin, destination, date, weather_locations (use route or weather parts when the user asked for alternatives/rebooking/route search or weather, OR described a disruption such as cancellation/delay/missed connection and asked what to do / what their options are)
        4) lookup_flight_by_number — flight_number, optional date
        5) lookup_flight_by_route — origin, destination, optional date — same rule as (3): include when they asked for other flights or rebooking, or after a disruption they asked what options they have; use IATA codes (infer from cities like London→LHR, New York→JFK when needed)
        6) lookup_weather — location — only when the user explicitly asked for weather or forecast

        Multi-step retrieval is allowed. Output must match the structured schema (action, capability, arguments_json, final_answer, etc.).
        """;

    private static string BuildUserTurn(
        string userMessage,
        ConversationRouteResult route,
        string workflowIntent,
        string planText,
        string prefLine,
        AgentTranscriptBuffer transcriptBuffer,
        string? priorConversationContext)
    {
        var topics = string.Join(", ", route.SuggestedRagTopics);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(priorConversationContext))
        {
            sb.AppendLine("Prior conversation (what was already said in this session — use for follow-up questions):");
            sb.AppendLine(priorConversationContext.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("User message:");
        sb.AppendLine(userMessage);
        sb.AppendLine();
        sb.AppendLine("Router context (hints):");
        sb.AppendLine($"- Intent: {route.Intent}");
        sb.AppendLine($"- Workflow: {workflowIntent}");
        sb.AppendLine($"- Suggested RAG topics: {topics}");
        sb.AppendLine($"- Reason: {route.Reason}");
        sb.AppendLine();
        sb.AppendLine("Plan (hint):");
        sb.AppendLine(planText);
        sb.AppendLine();
        sb.AppendLine("User preferences:");
        sb.AppendLine(prefLine);
        sb.AppendLine();
        sb.Append(transcriptBuffer.BuildTranscript("--- Execution log ---"));
        return sb.ToString();
    }

    private static string FormatPreferences(UserPreferences? p)
    {
        if (p is null) return "(defaults)";
        return $"risk={p.RiskTolerance}, meetingBufferMin={p.PreferredMeetingBufferMinutes}, homeAirport={p.HomeAirport}, preferredAirline={p.PreferredAirline}, remoteFallback={p.PrefersRemoteFallback}";
    }

    private static Dictionary<string, string?> ParseArgumentsJson(string? raw)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return d;
        try
        {
            using var doc = JsonDocument.Parse(raw.Trim());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return d;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                d[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => p.Value.GetRawText()
                };
            }
        }
        catch
        {
            /* ignore */
        }

        return d;
    }

    /// <summary>
    /// Lets the model answer follow-ups (times, status, same flight) from tools + prior chat without forcing
    /// policy retrieval when the router still marked NeedsRag for mixed threads.
    /// </summary>
    private static bool ShouldAllowOperationalAnswerWithoutPolicy(
        string userMessage,
        string? priorConversationContext,
        List<ToolExecutionResult> toolResults)
    {
        var msg = userMessage.Trim();
        if (msg.Length > 220)
            return false;

        var low = msg.ToLowerInvariant();
        if (low.Contains("policy", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("compensation", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("reimburs", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("eligible", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("rights", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("eu261", StringComparison.OrdinalIgnoreCase) ||
            (low.Contains("refund", StringComparison.OrdinalIgnoreCase) &&
             low.Contains("rule", StringComparison.OrdinalIgnoreCase)))
            return false;

        var hasFn = Regex.IsMatch(msg, @"\b[A-Z]{2}\s?\d{1,4}\b", RegexOptions.IgnoreCase);
        var looksOperational =
            low.Contains("arrival", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("landing", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("depart", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("departure", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("eta", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("etd", StringComparison.OrdinalIgnoreCase) ||
            low.Contains("gate", StringComparison.OrdinalIgnoreCase) ||
            (low.Contains("when", StringComparison.OrdinalIgnoreCase) &&
             (low.Contains("flight", StringComparison.OrdinalIgnoreCase) || hasFn)) ||
            low.Contains("what time", StringComparison.OrdinalIgnoreCase) ||
            (low.Contains("status", StringComparison.OrdinalIgnoreCase) && hasFn);

        if (!looksOperational)
            return false;

        var hasTool = toolResults.Exists(t => t.Success &&
            (t.ToolName.Contains("Flight", StringComparison.OrdinalIgnoreCase) ||
             t.ToolName.Contains("Weather", StringComparison.OrdinalIgnoreCase)));

        var prior = priorConversationContext?.Trim() ?? "";
        var hasPrior = prior.Length >= 60;

        return hasTool || hasPrior;
    }

    private static double Clamp01(double c)
    {
        if (double.IsNaN(c)) return 0.5;
        if (c < 0) return 0;
        if (c > 1) return 1;
        return c;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
