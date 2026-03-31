using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>
/// When the user sends a short follow-up (e.g. "arrival time for the alternative you found"),
/// flight numbers and airport codes may only appear in the prior assistant reply. This helper
/// merges that text into the buffer used only for regex-based tool signal extraction.
/// Hebrew substrings in <see cref="LooksLikeFlightRelatedFollowUp"/> match Hebrew user phrasing.
/// </summary>
public static class ConversationToolExtraction
{
    /// <summary>True if the message likely refers to a previous turn's flight/alternative data.</summary>
    public static bool LooksLikeFlightRelatedFollowUp(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var m = userMessage.ToLowerInvariant();

        if (m.Contains("arrival", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("arrive", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("departure", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("depart", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("landing", StringComparison.OrdinalIgnoreCase) ||
            m.Contains(" eta", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("scheduled time", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("actual time", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("rebook", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("the other flight", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("you found", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("you mentioned", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("you suggested", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("you said", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("that flight", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("the flight", StringComparison.OrdinalIgnoreCase) ||
            (m.Contains("earlier", StringComparison.OrdinalIgnoreCase) && m.Contains("flight", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (m.Contains("gate", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("terminal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (m.Contains("הגעה", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("נחיתה", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("המראה", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Appends the latest assistant message when the current line alone lacks codes but reads as a flight follow-up.
    /// </summary>
    public static string BuildToolSignalText(
        string userMessage,
        IReadOnlyList<ChatMessage> priorTurns,
        bool currentMessageHasFlightNumber,
        bool currentMessageHasTwoLocationCodes)
    {
        if (priorTurns.Count == 0)
            return userMessage;

        if (currentMessageHasFlightNumber && currentMessageHasTwoLocationCodes)
            return userMessage;

        if (!LooksLikeFlightRelatedFollowUp(userMessage))
            return userMessage;

        var lastAssistant = priorTurns
            .LastOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));
        if (lastAssistant?.Content is null || string.IsNullOrWhiteSpace(lastAssistant.Content))
            return userMessage;

        const int maxAssistantChars = 14_000;
        var body = lastAssistant.Content.Length <= maxAssistantChars
            ? lastAssistant.Content
            : lastAssistant.Content[..maxAssistantChars];

        return $"{userMessage.TrimEnd()}\n\n<<< prior_assistant_context >>>\n{body}";
    }
}
