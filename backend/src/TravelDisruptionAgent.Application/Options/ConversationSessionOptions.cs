namespace TravelDisruptionAgent.Application.Options;

/// <summary>Limits for session chat history (in-memory store + prompt injection).</summary>
public class ConversationSessionOptions
{
    public const string SectionName = "ConversationSession";

    /// <summary>Max messages retained per session after each append (oldest dropped first).</summary>
    public int MaxMessages { get; set; } = 5;

    /// <summary>Total characters of Content across stored messages; oldest dropped first.</summary>
    public int MaxTotalStoredChars { get; set; } = 16_000;

    /// <summary>Max characters for the formatted prior-conversation block in LLM prompts.</summary>
    public int MaxPromptChars { get; set; } = 12_000;
}
