using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>In-memory per-session message list. Thread-safe; for local demo.</summary>
public sealed class InMemoryConversationSessionStore : IConversationSessionStore
{
    private readonly ConversationSessionOptions _options;
    private readonly Dictionary<string, List<ChatMessage>> _sessions = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public InMemoryConversationSessionStore(IOptions<ConversationSessionOptions> options) =>
        _options = options.Value;

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ChatMessage>>(
                _sessions.TryGetValue(sessionKey, out var list) ? [.. list] : []);
        }
    }

    public Task AppendMessageAsync(string sessionKey, ChatMessage message, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionKey, out var list))
            {
                list = [];
                _sessions[sessionKey] = list;
            }

            list.Add(message);
            Trim(list);
        }

        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _sessions.Remove(sessionKey);
        }

        return Task.CompletedTask;
    }

    private void Trim(List<ChatMessage> list)
    {
        while (list.Count > _options.MaxMessages)
            list.RemoveAt(0);

        while (TotalChars(list) > _options.MaxTotalStoredChars && list.Count > 0)
            list.RemoveAt(0);
    }

    private static int TotalChars(List<ChatMessage> list) =>
        list.Sum(m => m.Content.Length);
}
