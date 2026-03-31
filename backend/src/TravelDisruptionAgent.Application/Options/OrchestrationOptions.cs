namespace TravelDisruptionAgent.Application.Options;

/// <summary>
/// Cross-cutting orchestration behaviour (tool backfill, session-scoped fallbacks).
/// </summary>
public class OrchestrationOptions
{
    public const string SectionName = "Orchestration";

    /// <summary>
    /// When true, a follow-up request may synthesize a successful <c>FlightLookupByRoute</c> tool row
    /// from the <b>prior assistant message text</b> loaded from <see cref="IMemoryService"/> for the same session.
    /// Trust boundary: only messages persisted by this API as role <c>assistant</c> are considered — not user-supplied JSON.
    /// Disable in high-assurance deployments if you prefer never to treat historical prose as tool evidence.
    /// </summary>
    public bool EnableConversationHistoryRouteFallback { get; set; } = true;

    /// <summary>
    /// When true, execution-trace and route-filter logs omit tool parameters, fact text, and flight-number samples (counts only).
    /// Enable in Production to reduce PII in application logs.
    /// </summary>
    public bool RedactSensitiveOrchestrationLogs { get; set; } = true;
}
