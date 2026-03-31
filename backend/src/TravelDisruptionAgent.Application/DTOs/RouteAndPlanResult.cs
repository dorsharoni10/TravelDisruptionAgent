using TravelDisruptionAgent.Application.Interfaces;

namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>Single LLM turn: routing + short plan (replaces separate route + plan calls when unified succeeds).</summary>
public sealed record RouteAndPlanResult(
    ConversationRouteResult Route,
    AgentPlan Plan,
    /// <summary>Non-null when unified LLM failed or plan parsing fell back to deterministic planning.</summary>
    string? UnifiedFallbackReason);
