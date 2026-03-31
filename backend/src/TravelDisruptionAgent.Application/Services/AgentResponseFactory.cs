using System.Diagnostics;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Domain.Enums;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>Builds <see cref="AgentResponse"/> for <see cref="AgentOrchestrator"/>.</summary>
public static class AgentResponseFactory
{
    public static AgentResponse BuildResponse(
        bool inScope, string intent, string workflowIntent, string summary,
        List<string> plan, List<ToolExecutionResult> tools,
        List<SelfCorrectionStep> corrections, string recommendation,
        List<string> warnings, double confidence, List<string> dataSources,
        string traceId, Stopwatch sw, List<AgentEvent> events,
        bool memoryUsed, bool ragUsed, string routingDecision, List<string> ragContext,
        List<string>? llmErrors = null,
        bool agenticLoopUsed = false,
        AgentLoopRunResult? agentic = null,
        string sessionId = "",
        IReadOnlyList<ChatMessage>? conversationHistory = null)
    {
        sw.Stop();
        return new AgentResponse
        {
            SessionId = sessionId,
            ConversationHistory = conversationHistory is null ? [] : [..conversationHistory],
            InScope = inScope,
            Intent = intent,
            WorkflowIntent = workflowIntent,
            Summary = summary,
            AgentPlan = plan,
            ToolExecutions = tools,
            SelfCorrectionSteps = corrections,
            FinalRecommendation = recommendation,
            Warnings = warnings,
            Confidence = Math.Round(confidence, 2),
            DataSources = dataSources,
            TraceId = traceId,
            DurationMs = sw.ElapsedMilliseconds,
            Events = events,
            MemoryUsed = memoryUsed,
            RagUsed = ragUsed,
            RoutingDecision = routingDecision,
            RagContext = ragContext,
            LlmErrors = llmErrors ?? [],
            AgenticLoopUsed = agenticLoopUsed,
            AgenticStopReason = agentic?.StopReason.ToString(),
            AgenticIterations = agentic?.IterationCount ?? 0,
            AgenticPolicyRetrievalCount = agentic?.PolicyRetrievalCount ?? 0,
            AgenticPolicyQueries = agentic?.PolicyQueriesExecuted ?? [],
            AgenticTrace = agentic?.Trace,
            AnswerGroundedOnPolicy = agentic?.AnswerGroundedOnPolicy ?? false,
            AnswerGroundedOnTools = agentic?.AnswerGroundedOnTools ?? false,
            AgenticStructuredLlmOnly = agentic?.UsedStructuredLlmOutput ?? false,
            AgenticPolicyGroundingFallback = agentic?.PolicyGroundingFallbackApplied ?? false,
            AgenticCitationWarnings = agentic?.CitationSanitizationWarnings ?? []
        };
    }

    public static List<string> GetDataSources(List<ToolExecutionResult> toolResults, bool ragUsed = false)
    {
        var sources = toolResults.Select(t => t.DataSource).Distinct().ToList();
        if (ragUsed) sources.Add("Policy Knowledge Base");
        return sources;
    }
}
