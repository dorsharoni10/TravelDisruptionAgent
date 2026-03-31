namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>One turn in a session-scoped conversation.</summary>
public record ChatMessage(string Role, string Content, DateTime TimestampUtc);
