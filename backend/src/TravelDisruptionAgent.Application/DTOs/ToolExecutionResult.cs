namespace TravelDisruptionAgent.Application.DTOs;

public record ToolExecutionResult(
    string ToolName,
    string Input,
    string Output,
    bool Success,
    string DataSource,
    long DurationMs,
    string? ErrorMessage = null,
    string? Warning = null
);
