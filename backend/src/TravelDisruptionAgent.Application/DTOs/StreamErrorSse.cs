namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>Payload for SSE <c>event: stream_error</c> when the chat pipeline fails.</summary>
public record StreamErrorSse(string Code, string Message);
