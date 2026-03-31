using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Services;
using TravelDisruptionAgent.Domain.Entities;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IRecommendationService
{
    Task<RecommendationResult> GenerateAsync(
        string userMessage,
        string intent,
        List<ToolExecutionResult> toolResults,
        UserPreferences? preferences = null,
        IReadOnlyList<string>? ragContext = null,
        PlanValidationResult? validation = null,
        DateTime? requestTime = null,
        string? priorConversationContext = null,
        CancellationToken cancellationToken = default);
}

public record RecommendationResult(
    string Recommendation,
    double Confidence,
    List<string> Warnings,
    bool LlmFailed = false,
    string? LlmErrorDetail = null
);
