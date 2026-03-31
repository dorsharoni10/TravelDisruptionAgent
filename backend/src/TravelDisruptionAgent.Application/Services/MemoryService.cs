using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Domain.Entities;

namespace TravelDisruptionAgent.Application.Services;

public class MemoryService : IMemoryService
{
    private readonly ConcurrentDictionary<string, UserPreferences> _preferences = new();
    private readonly object _preferencesFileLock = new();
    private readonly IConversationSessionStore _sessions;
    private readonly string _dataPath;
    private readonly ILogger<MemoryService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MemoryService(IConversationSessionStore sessions, ILogger<MemoryService> logger)
    {
        _sessions = sessions;
        _logger = logger;
        _dataPath = Path.Combine(AppContext.BaseDirectory, "data", "preferences");
        Directory.CreateDirectory(_dataPath);
        LoadPersistedPreferences();
    }

    public Task<UserPreferences> GetPreferencesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var prefs = _preferences.GetOrAdd(userId, _ => new UserPreferences { UserId = userId });
        return Task.FromResult(prefs);
    }

    public Task SavePreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        preferences.UpdatedAt = DateTime.UtcNow;
        lock (_preferencesFileLock)
        {
            _preferences[preferences.UserId] = preferences;
            PersistPreferences(preferences);
        }

        _logger.LogDebug("Saved preferences for user {UserId}", preferences.UserId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessage>> GetConversationHistoryAsync(string sessionKey, CancellationToken cancellationToken = default) =>
        _sessions.GetMessagesAsync(sessionKey, cancellationToken);

    public Task AppendMessageAsync(string sessionKey, ChatMessage message, CancellationToken cancellationToken = default) =>
        _sessions.AppendMessageAsync(sessionKey, message, cancellationToken);

    public Task ClearConversationAsync(string sessionKey, CancellationToken cancellationToken = default) =>
        _sessions.ClearSessionAsync(sessionKey, cancellationToken);

    private void LoadPersistedPreferences()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_dataPath, "*.json"))
            {
                var json = File.ReadAllText(file);
                var prefs = JsonSerializer.Deserialize<UserPreferences>(json, JsonOpts);
                if (prefs is not null)
                {
                    _preferences[prefs.UserId] = prefs;
                    _logger.LogDebug("Loaded persisted preferences for {UserId}", prefs.UserId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load persisted preferences");
        }
    }

    private void PersistPreferences(UserPreferences prefs)
    {
        try
        {
            var safeName = string.Join("_", prefs.UserId.Split(Path.GetInvalidFileNameChars()));
            var filePath = Path.Combine(_dataPath, $"{safeName}.json");
            var json = JsonSerializer.Serialize(prefs, JsonOpts);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist preferences for {UserId}", prefs.UserId);
        }
    }
}
