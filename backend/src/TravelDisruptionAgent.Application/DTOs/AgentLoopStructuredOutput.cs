using System.Text.Json.Serialization;

namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>
/// JSON shape for structured agent steps (Gemini responseSchema / OpenAI json_schema).
/// Tool arguments are a JSON object serialized as a string for strict schema compatibility.
/// </summary>
public sealed class AgentLoopStructuredOutput
{
    [JsonPropertyName("thought")]
    public string Thought { get; set; } = "";

    [JsonPropertyName("known_summary")]
    public string KnownSummary { get; set; } = "";

    [JsonPropertyName("still_missing")]
    public string StillMissing { get; set; } = "";

    [JsonPropertyName("sufficient_evidence")]
    public bool SufficientEvidence { get; set; }

    /// <summary>invoke_tool or final_answer</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = "";

    /// <summary>Serialized JSON object for tool arguments, e.g. {"query":"meals"}</summary>
    [JsonPropertyName("arguments_json")]
    public string ArgumentsJson { get; set; } = "{}";

    [JsonPropertyName("final_answer")]
    public string FinalAnswer { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.85;
}
