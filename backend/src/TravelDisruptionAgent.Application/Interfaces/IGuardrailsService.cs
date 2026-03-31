using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IGuardrailsService
{
    Task<GuardrailResult> ValidateInputAsync(string userMessage, CancellationToken cancellationToken = default);
    Task<GuardrailResult> ValidateOutputAsync(string agentResponse, CancellationToken cancellationToken = default);
    FactualAccuracyResult ValidateFactualAccuracy(string responseText, VerifiedContext ctx);
}

public record GuardrailResult(bool IsValid, string? Reason = null);

public record FactualAccuracyResult(
    bool Passed,
    string CorrectedText,
    List<FactualViolation> Violations);

public record FactualViolation(
    string Rule,
    string Description,
    FactualViolationSeverity Severity);

public enum FactualViolationSeverity
{
    Warning,
    Corrected,
    Blocked
}
