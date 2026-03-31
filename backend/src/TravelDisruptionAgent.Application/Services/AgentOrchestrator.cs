using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Utilities;
using TravelDisruptionAgent.Domain.Enums;

namespace TravelDisruptionAgent.Application.Services;

public class AgentOrchestrator : IAgentOrchestrator
{
    private static readonly ActivitySource Telemetry = new("TravelDisruptionAgent.Orchestrator");

    private readonly IConversationRouter _conversationRouter;
    private readonly IRagService _ragService;
    private readonly IRecommendationService _recommendationService;
    private readonly ISelfCorrectionService _selfCorrectionService;
    private readonly IGuardrailsService _guardrailsService;
    private readonly IMemoryService _memoryService;
    private readonly IKernelFactory _kernelFactory;
    private readonly IAgentLoopService _agentLoopService;
    private readonly AgentToolExecutionPipeline _toolPipeline;
    private readonly AgenticOptions _agenticOptions;
    private readonly ConversationSessionOptions _sessionOptions;
    private readonly OrchestrationOptions _orchestrationOptions;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IConversationRouter conversationRouter,
        AgentToolExecutionPipeline toolPipeline,
        IRagService ragService,
        IRecommendationService recommendationService,
        ISelfCorrectionService selfCorrectionService,
        IGuardrailsService guardrailsService,
        IMemoryService memoryService,
        IKernelFactory kernelFactory,
        IAgentLoopService agentLoopService,
        IOptions<AgenticOptions> agenticOptions,
        IOptions<ConversationSessionOptions> sessionOptions,
        IOptions<OrchestrationOptions> orchestrationOptions,
        ILogger<AgentOrchestrator> logger)
    {
        _conversationRouter = conversationRouter;
        _toolPipeline = toolPipeline;
        _ragService = ragService;
        _recommendationService = recommendationService;
        _selfCorrectionService = selfCorrectionService;
        _guardrailsService = guardrailsService;
        _memoryService = memoryService;
        _kernelFactory = kernelFactory;
        _agentLoopService = agentLoopService;
        _agenticOptions = agenticOptions.Value;
        _sessionOptions = sessionOptions.Value;
        _orchestrationOptions = orchestrationOptions.Value;
        _logger = logger;
    }

    public async Task<AgentResponse> ProcessAsync(
        ChatRequest request,
        Func<AgentEvent, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartActivity("agent.process");
        activity?.SetTag("user.id", request.UserId);
        if (string.IsNullOrEmpty(request.Message))
            throw new ArgumentException("Message is required and cannot be empty.", nameof(request));
        activity?.SetTag("message.length", request.Message.Length);

        var clientSessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId.Trim();

        var sessionKey = string.IsNullOrWhiteSpace(request.SessionStorageNamespace)
            ? clientSessionId
            : $"{request.SessionStorageNamespace.Trim()}\u001f{clientSessionId}";

        activity?.SetTag("session.id", clientSessionId);
        activity?.SetTag("session.storage_key", sessionKey);

        var priorTurns = await _memoryService.GetConversationHistoryAsync(sessionKey, cancellationToken);
        var priorPrompt = ConversationHistoryPrompt.Build(
            priorTurns,
            _sessionOptions.MaxMessages,
            _sessionOptions.MaxPromptChars);

        async Task PersistTurnAsync(string assistantReply, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            await _memoryService.AppendMessageAsync(sessionKey, new ChatMessage("user", request.Message, now), ct);
            await _memoryService.AppendMessageAsync(sessionKey, new ChatMessage("assistant", assistantReply, now), ct);
        }

        var sw = Stopwatch.StartNew();
        var requestTime = DateTime.UtcNow;
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")[..16];
        var events = new List<AgentEvent>();
        bool memoryUsed = false;
        bool ragUsed = false;
        string routingReason = "";
        List<string> ragContext = [];
        var useAgenticPath = false;
        AgentLoopRunResult? agentLoopOutcome = null;

        async Task Emit(AgentStepType step, string title, string content)
        {
            var evt = AgentEvent.Create(step, title, content);
            events.Add(evt);
            if (onEvent is not null) await onEvent(evt);
        }

        // Step 1: Input guardrail
        await Emit(AgentStepType.Guardrail, "Input Validation", "Validating input...");
        var guardrail = await _guardrailsService.ValidateInputAsync(request.Message, cancellationToken);
        if (!guardrail.IsValid)
        {
            await Emit(AgentStepType.Guardrail, "Input Rejected", guardrail.Reason!);
            await PersistTurnAsync(guardrail.Reason!, cancellationToken);
            var hist1 = await _memoryService.GetConversationHistoryAsync(sessionKey, cancellationToken);
            return AgentResponseFactory.BuildResponse(false, "invalid_input", "", guardrail.Reason!,
                [], [], [], guardrail.Reason!, [], 0, [], traceId, sw, events,
                false, false, "Input rejected by guardrail", [],
                sessionId: clientSessionId, conversationHistory: hist1);
        }

        var llmErrors = new List<string>();

        // Step 2–5: Unified scope + routing + plan (one LLM call when configured)
        await Emit(AgentStepType.ScopeCheck, "Conversation Routing", "Analyzing scope, intent, data needs, and plan...");
        var routePlan = await _conversationRouter.RouteAndPlanAsync(request.Message, priorPrompt, cancellationToken);
        var route = routePlan.Route;
        var plan = routePlan.Plan;

        if (routePlan.UnifiedFallbackReason is not null)
            llmErrors.Add(routePlan.UnifiedFallbackReason);

        if (route.LlmFallbackReason is not null)
        {
            llmErrors.Add($"Routing: {route.LlmFallbackReason}");
            await Emit(AgentStepType.ScopeCheck, "LLM Unavailable — Signal Fallback",
                $"LLM failed: {route.LlmFallbackReason}. Used semantic + structural routing instead.");
        }

        if (!route.InScope)
        {
            var msg = route.RejectionMessage ?? "This query is outside the travel disruption domain.";
            await Emit(AgentStepType.ScopeCheck, "Out of Scope", msg);
            await PersistTurnAsync(msg, cancellationToken);
            var hist2 = await _memoryService.GetConversationHistoryAsync(sessionKey, cancellationToken);
            return AgentResponseFactory.BuildResponse(false, route.Intent, "", msg,
                [], [], [], msg, [], 1.0, [], traceId, sw, events,
                false, false, "Out of scope — no routing needed", [],
                sessionId: clientSessionId, conversationHistory: hist2);
        }

        var workflowIntent = IntentCatalog.ToWorkflowIntent(route.Intent, request.Message);

        activity?.SetTag("scope.classification", "in_scope");
        activity?.SetTag("scope.intent", route.Intent);
        activity?.SetTag("scope.workflow_intent", workflowIntent);
        await Emit(AgentStepType.ScopeCheck, "In Scope",
            $"Intent: {route.Intent} → workflow: {workflowIntent}");

        // Step 3: Load user preferences (memory)
        await Emit(AgentStepType.Memory, "Loading Preferences", "Checking user memory...");
        var preferences = await _memoryService.GetPreferencesAsync(request.UserId, cancellationToken);
        memoryUsed = !string.IsNullOrEmpty(preferences.PreferredAirline) ||
                     !string.IsNullOrEmpty(preferences.HomeAirport) ||
                     preferences.RiskTolerance != "moderate" ||
                     preferences.PrefersRemoteFallback ||
                     preferences.PreferredMeetingBufferMinutes != 60;

        if (memoryUsed)
            await Emit(AgentStepType.Memory, "Preferences Loaded",
                $"Risk: {preferences.RiskTolerance}, Buffer: {preferences.PreferredMeetingBufferMinutes}min" +
                (!string.IsNullOrEmpty(preferences.PreferredAirline) ? $", Airline: {preferences.PreferredAirline}" : ""));
        else
            await Emit(AgentStepType.Memory, "No Custom Preferences", "Using default settings");

        await Emit(AgentStepType.Routing, "Routing Decision", "Deciding data strategy...");
        routingReason = $"{route.Reason} | needsTools={route.NeedsTools}, needsRag={route.NeedsRag}";

        var retrievePolicyContext = route.NeedsRag || route.NeedsTools;

        string routingLabel = retrievePolicyContext
            ? (route.NeedsTools ? "Tools + RAG" : "Policy RAG")
            : "Internal Knowledge";
        activity?.SetTag("routing.decision", routingLabel);
        activity?.SetTag("routing.use_tools", route.NeedsTools);
        activity?.SetTag("routing.use_rag", retrievePolicyContext);
        activity?.SetTag("memory.used", memoryUsed);
        await Emit(AgentStepType.Routing, routingLabel, routingReason);

        if (plan.LlmFallbackReason is not null)
        {
            llmErrors.Add($"Planning: {plan.LlmFallbackReason}");
            await Emit(AgentStepType.Planning, "Planning Fallback",
                $"Planning note: {plan.LlmFallbackReason}");
        }

        var planDisplay = $"Goal: {plan.Goal}\n" +
                          string.Join("\n", plan.Steps.Select((s, i) => $"  {i + 1}. {s}"));
        await Emit(AgentStepType.Planning, "Plan Ready", planDisplay);

        // Optional: full agentic RAG loop (model picks tools + retrievals each step). Safe fallback to legacy pipeline.
        if (_agenticOptions.EnableAgenticRag && _kernelFactory.IsConfigured)
        {
            try
            {
                await Emit(AgentStepType.AgentReasoning, "Agentic RAG",
                    "Starting model-driven tool and knowledge retrieval loop…");
                agentLoopOutcome = await _agentLoopService.RunAsync(
                    request.Message, route, workflowIntent, plan.Steps.ToList(), preferences,
                    Emit, priorPrompt, cancellationToken);
                activity?.SetTag("agentic.succeeded", agentLoopOutcome.Succeeded);
                activity?.SetTag("agentic.stop_reason", agentLoopOutcome.StopReason.ToString());
                activity?.SetTag("agentic.iterations", agentLoopOutcome.IterationCount);
                activity?.SetTag("agentic.policy_retrievals", agentLoopOutcome.PolicyRetrievalCount);
                _logger.LogInformation(
                    "Agentic RAG: succeeded={Ok}, stop={Stop}, iterations={Iter}, retrievals={Ret}, toolRows={Tools}, policyQueries=[{Q}]",
                    agentLoopOutcome.Succeeded,
                    agentLoopOutcome.StopReason,
                    agentLoopOutcome.IterationCount,
                    agentLoopOutcome.PolicyRetrievalCount,
                    agentLoopOutcome.ToolResults.Count,
                    string.Join(" | ", agentLoopOutcome.PolicyQueriesExecuted));
                if (agentLoopOutcome.Succeeded && !string.IsNullOrWhiteSpace(agentLoopOutcome.FinalAnswer))
                    useAgenticPath = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agentic RAG failed — using legacy tool/RAG pipeline");
            }
        }

        // Step 6: Tool execution (legacy path, or when agentic loop disabled / failed)
        var toolResults = new List<ToolExecutionResult>();
        var selfCorrectionSteps = new List<SelfCorrectionStep>();

        if (useAgenticPath && agentLoopOutcome is not null)
        {
            toolResults.AddRange(agentLoopOutcome.ToolResults);
            ragContext = agentLoopOutcome.PolicyChunks.ToList();
            ragUsed = ragContext.Count > 0;
            routingReason =
                $"{routingReason} | agentic=used; stop={agentLoopOutcome.StopReason}; iterations={agentLoopOutcome.IterationCount}; policyRetrievals={agentLoopOutcome.PolicyRetrievalCount}";
        }
        else if (route.NeedsTools)
        {
            await _toolPipeline.ExecuteLegacyToolsAsync(
                request.Message, priorTurns, workflowIntent, toolResults,
                selfCorrectionSteps, Emit, requestTime, cancellationToken);
        }

        // Step 6.5: Plan-Execution validation — detect missing tools and contradictions
        await Emit(AgentStepType.Verification, "Plan Validation", "Comparing plan vs. executed steps...");
        var validation = PlanExecutionValidator.Validate(plan.Steps, toolResults, workflowIntent, request.Message);

        if (!validation.IsFullyAligned)
        {
            var issuesSummary = string.Join("\n",
                validation.Issues.Select(i => $"- [{i.Type}] {i.Description}"));
            await Emit(AgentStepType.Verification, "Alignment Issues Found", issuesSummary);
        }

        validation = await _toolPipeline.TryApplyRouteBackfillAsync(
            validation,
            plan.Steps,
            workflowIntent,
            request.Message,
            priorTurns,
            toolResults,
            selfCorrectionSteps,
            Emit,
            requestTime,
            cancellationToken);

        var lacksSuccessfulRouteLookup = !toolResults.Any(t =>
            t.ToolName == "FlightLookupByRoute" && t.Success);
        if (lacksSuccessfulRouteLookup &&
            (validation.MissingTools.Contains("FlightLookupByRoute") ||
             HistoryFollowUpSignals.LikelyReferencesPriorFlightDetail(request.Message)) &&
            ConversationHistoryRouteFallback.TryAppendSyntheticRouteTool(
                toolResults,
                priorTurns,
                _orchestrationOptions.EnableConversationHistoryRouteFallback,
                _logger))
        {
            validation = PlanExecutionValidator.Validate(plan.Steps, toolResults, workflowIntent, request.Message);
            selfCorrectionSteps.Add(new SelfCorrectionStep(
                "No live route tool result in this request",
                "Recovered alternative flight times from prior assistant message in session (trust: IMemoryService assistant role only)",
                "FlightLookupByRoute marked from conversation_history_fallback",
                DateTime.UtcNow));
        }

        if (!validation.IsFullyAligned)
        {
            await Emit(AgentStepType.Verification, "Post-Backfill Validation",
                $"Remaining issues: {validation.Issues.Count} " +
                $"(contradictions: {validation.Contradictions.Count}, missing: {validation.MissingTools.Count})");
        }
        else
        {
            await Emit(AgentStepType.Verification, "Plan Aligned", "All planned steps executed successfully");
        }

        // Step 7: RAG retrieval (legacy single-shot; skipped when agentic loop already retrieved policy)
        if (!useAgenticPath && retrievePolicyContext)
        {
            var ragTopK = route.NeedsTools && !route.NeedsRag ? 2 : 3;
            await Emit(AgentStepType.Rag, "Policy Retrieval", "Searching knowledge base...");
            var retrieved = await _ragService.RetrieveRelevantContextAsync(
                request.Message, ragTopK, cancellationToken);
            ragContext = retrieved.ToList();
            ragUsed = ragContext.Count > 0;

            if (ragUsed)
                await Emit(AgentStepType.Rag, "Policy Found",
                    $"Retrieved {ragContext.Count} relevant policy document(s)");
            else
                await Emit(AgentStepType.Rag, "No Policy Match",
                    "No matching policy documents found");
        }

        // Step 7.5: Build verified context & emit observability logs
        var verifiedCtx = VerifiedContextBuilder.Build(
            workflowIntent, toolResults, ragUsed ? ragContext : null, validation,
            requestTime: requestTime);

        var toolResultsForClient =
            VerifiedContextBuilder.ToDisplayToolResults(toolResults, verifiedCtx, requestTime);

        OrchestrationTraceLogger.LogExecutionTrace(
            _logger,
            plan.Steps.ToList(),
            toolResults,
            verifiedCtx,
            _orchestrationOptions.RedactSensitiveOrchestrationLogs);

        // Step 8: Generate recommendation (with preferences + RAG + validation)
        await Emit(AgentStepType.FinalAnswer, "Generating Recommendation", "Synthesizing results...");
        RecommendationResult recommendation;
        if (useAgenticPath && agentLoopOutcome is not null)
        {
            var rw = new List<string>();
            if (agentLoopOutcome.PolicyRetrievalCount == 0 && route.NeedsRag)
                rw.Add("Agentic loop did not retrieve policy passages; avoid stating undocumented company policy.");
            recommendation = new RecommendationResult(
                agentLoopOutcome.FinalAnswer!, agentLoopOutcome.Confidence, rw);
        }
        else
        {
            recommendation = await _recommendationService.GenerateAsync(
                request.Message, workflowIntent, toolResults,
                memoryUsed ? preferences : null,
                ragUsed ? ragContext : null,
                validation,
                requestTime,
                priorPrompt,
                cancellationToken);
        }

        if (recommendation.LlmFailed)
        {
            llmErrors.Add($"Recommendation: {recommendation.LlmErrorDetail}");
            await Emit(AgentStepType.FinalAnswer, "LLM Unavailable — No AI Recommendation",
                $"LLM failed: {recommendation.LlmErrorDetail}. " +
                "Template fallback was available but intentionally not used for transparency.");
        }

        // Step 9: Self-correction review (with validation context)
        await Emit(AgentStepType.Verification, "Verification", "Reviewing accuracy and consistency...");
        var correction = await _selfCorrectionService.ReviewAndCorrectAsync(
            request.Message, toolResults, recommendation.Recommendation,
            recommendation.Confidence, validation, cancellationToken);

        foreach (var step in correction.Steps)
        {
            selfCorrectionSteps.Add(step);
            await Emit(AgentStepType.SelfCorrection, step.Issue, $"{step.Action} → {step.Outcome}");
        }

        // Step 10: Output guardrail (length / safety)
        var outputGuardrail = await _guardrailsService.ValidateOutputAsync(
            correction.FinalRecommendation, cancellationToken);

        if (!outputGuardrail.IsValid)
        {
            await Emit(AgentStepType.Guardrail, "Output Filtered", outputGuardrail.Reason!);
            const string filteredUserMsg = "The response was filtered. Please rephrase your query.";
            await PersistTurnAsync(filteredUserMsg, cancellationToken);
            var hist3 = await _memoryService.GetConversationHistoryAsync(sessionKey, cancellationToken);
            return AgentResponseFactory.BuildResponse(true, route.Intent, workflowIntent, "Response filtered for safety.",
                plan.Steps.ToList(), toolResultsForClient, selfCorrectionSteps,
                filteredUserMsg,
                correction.Warnings, 0, AgentResponseFactory.GetDataSources(toolResults, ragUsed), traceId, sw, events,
                memoryUsed, ragUsed, routingReason, ragContext, llmErrors, useAgenticPath, agentLoopOutcome,
                sessionId: clientSessionId, conversationHistory: hist3);
        }

        // Step 10.5: Factual accuracy guardrail
        var factualResult = _guardrailsService.ValidateFactualAccuracy(
            correction.FinalRecommendation, verifiedCtx);

        var finalText = factualResult.CorrectedText;

        if (factualResult.Violations.Count > 0)
        {
            var violationSummary = string.Join("\n",
                factualResult.Violations.Select(v => $"- [{v.Severity}] {v.Rule}: {v.Description}"));
            await Emit(AgentStepType.Guardrail, "Factual Accuracy Check", violationSummary);

            foreach (var v in factualResult.Violations)
            {
                selfCorrectionSteps.Add(new SelfCorrectionStep(
                    $"Factual guardrail: {v.Rule}",
                    v.Description,
                    v.Severity.ToString(),
                    DateTime.UtcNow));

                if (v.Severity == FactualViolationSeverity.Warning)
                    correction.Warnings.Add($"[Guardrail] {v.Description}");
            }
        }
        else
        {
            await Emit(AgentStepType.Guardrail, "Factual Accuracy", "All claims verified against tool results");
        }

        if (!factualResult.Passed)
        {
            await Emit(AgentStepType.Guardrail, "Factual Accuracy Blocked",
                "Response blocked due to critical factual violations.");
            const string blockedUserMsg =
                "The response contained claims that could not be verified. Please try again.";
            await PersistTurnAsync(blockedUserMsg, cancellationToken);
            var hist4 = await _memoryService.GetConversationHistoryAsync(sessionKey, cancellationToken);
            return AgentResponseFactory.BuildResponse(true, route.Intent, workflowIntent, "Response blocked for factual accuracy.",
                plan.Steps.ToList(), toolResultsForClient, selfCorrectionSteps,
                blockedUserMsg,
                correction.Warnings, 0, AgentResponseFactory.GetDataSources(toolResults, ragUsed), traceId, sw, events,
                memoryUsed, ragUsed, routingReason, ragContext, llmErrors, useAgenticPath, agentLoopOutcome,
                sessionId: clientSessionId, conversationHistory: hist4);
        }

        // Step 11: Final answer
        await Emit(AgentStepType.FinalAnswer, "Final Recommendation", finalText);
        await PersistTurnAsync(finalText, cancellationToken);

        activity?.SetTag("result.confidence", correction.AdjustedConfidence);
        activity?.SetTag("result.tools_count", toolResults.Count);
        activity?.SetTag("result.corrections_count", selfCorrectionSteps.Count);
        activity?.SetTag("result.rag_used", ragUsed);
        activity?.SetTag("result.factual_violations", factualResult.Violations.Count);
        activity?.SetTag("result.duration_ms", sw.ElapsedMilliseconds);

        var hist5 = await _memoryService.GetConversationHistoryAsync(sessionKey, cancellationToken);
        return AgentResponseFactory.BuildResponse(true, route.Intent, workflowIntent,
            $"Analyzed: {route.Intent.Replace('_', ' ')} ({workflowIntent.Replace('_', ' ')})",
            plan.Steps.ToList(), toolResultsForClient, selfCorrectionSteps,
            finalText, correction.Warnings,
            correction.AdjustedConfidence, AgentResponseFactory.GetDataSources(toolResults, ragUsed),
            traceId, sw, events, memoryUsed, ragUsed, routingReason, ragContext, llmErrors,
            useAgenticPath, agentLoopOutcome, sessionId: clientSessionId, conversationHistory: hist5);
    }
}
