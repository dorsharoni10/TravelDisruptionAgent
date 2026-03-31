using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Services;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface ISelfCorrectionService
{
    Task<SelfCorrectionResult> ReviewAndCorrectAsync(
        string userMessage,
        List<ToolExecutionResult> toolResults,
        string draftRecommendation,
        double initialConfidence,
        PlanValidationResult? validation = null,
        CancellationToken cancellationToken = default);
}

public record SelfCorrectionResult(
    string FinalRecommendation,
    double AdjustedConfidence,
    List<SelfCorrectionStep> Steps,
    List<string> Warnings
);
