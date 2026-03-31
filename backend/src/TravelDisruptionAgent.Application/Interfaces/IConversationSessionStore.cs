using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

/// <summary>Session-scoped chat messages. Default: in-memory; swap for Redis/DB via DI.</summary>
public interface IConversationSessionStore
{
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionKey, CancellationToken cancellationToken = default);

    Task AppendMessageAsync(string sessionKey, ChatMessage message, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default);
}
