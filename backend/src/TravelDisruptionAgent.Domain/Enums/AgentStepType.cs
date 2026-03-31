namespace TravelDisruptionAgent.Domain.Enums;

public enum AgentStepType
{
    ScopeCheck,
    Routing,
    /// <summary>Agentic loop: model reasoning / action choice (not full chain-of-thought to user).</summary>
    AgentReasoning,
    Planning,
    ToolCall,
    Rag,
    Memory,
    Verification,
    SelfCorrection,
    Guardrail,
    FinalAnswer
}
