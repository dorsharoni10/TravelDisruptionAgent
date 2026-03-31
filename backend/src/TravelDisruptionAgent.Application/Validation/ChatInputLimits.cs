namespace TravelDisruptionAgent.Application.Validation;

/// <summary>Single source for chat message length limits (API validation + docs).</summary>
public static class ChatInputLimits
{
    public const int MaxMessageLength = 32_000;
}
