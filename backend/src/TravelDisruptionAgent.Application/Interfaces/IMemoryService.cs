using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Domain.Entities;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IMemoryService
{
    Task<UserPreferences> GetPreferencesAsync(string userId, CancellationToken cancellationToken = default);
    Task SavePreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessage>> GetConversationHistoryAsync(string sessionKey, CancellationToken cancellationToken = default);

    Task AppendMessageAsync(string sessionKey, ChatMessage message, CancellationToken cancellationToken = default);

    Task ClearConversationAsync(string sessionKey, CancellationToken cancellationToken = default);
}
