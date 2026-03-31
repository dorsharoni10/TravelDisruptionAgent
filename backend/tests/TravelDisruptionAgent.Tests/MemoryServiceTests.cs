using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using TravelDisruptionAgent.Domain.Entities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class MemoryServiceTests
{
    private static MemoryService CreateService()
    {
        var store = new InMemoryConversationSessionStore(Options.Create(new ConversationSessionOptions()));
        return new MemoryService(store, NullLogger<MemoryService>.Instance);
    }

    private readonly MemoryService _service = CreateService();

    [Fact]
    public async Task GetPreferences_NewUser_ShouldReturnDefaults()
    {
        var prefs = await _service.GetPreferencesAsync("new-user-123");
        prefs.Should().NotBeNull();
        prefs.UserId.Should().Be("new-user-123");
        prefs.RiskTolerance.Should().Be("moderate");
        prefs.PreferredMeetingBufferMinutes.Should().Be(60);
    }

    [Fact]
    public async Task SaveAndGet_ShouldPersistPreferences()
    {
        var prefs = new UserPreferences
        {
            UserId = "test-user-save",
            PreferredAirline = "Delta",
            RiskTolerance = "safe",
            HomeAirport = "JFK",
            PreferredMeetingBufferMinutes = 90,
            PrefersRemoteFallback = true
        };

        await _service.SavePreferencesAsync(prefs);
        var loaded = await _service.GetPreferencesAsync("test-user-save");

        loaded.PreferredAirline.Should().Be("Delta");
        loaded.RiskTolerance.Should().Be("safe");
        loaded.HomeAirport.Should().Be("JFK");
        loaded.PreferredMeetingBufferMinutes.Should().Be(90);
        loaded.PrefersRemoteFallback.Should().BeTrue();
    }

    [Fact]
    public async Task SavePreferences_ShouldUpdateTimestamp()
    {
        var prefs = new UserPreferences { UserId = "test-user-ts" };
        var before = DateTime.UtcNow.AddSeconds(-1);

        await _service.SavePreferencesAsync(prefs);
        var loaded = await _service.GetPreferencesAsync("test-user-ts");

        loaded.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task ConversationRoundTrip_ShouldRetainMessagesWithinSession()
    {
        const string session = "sess-1";
        var t = DateTime.UtcNow;
        await _service.AppendMessageAsync(session, new ChatMessage("user", "Hi", t));
        await _service.AppendMessageAsync(session, new ChatMessage("assistant", "Hello", t));
        var hist = await _service.GetConversationHistoryAsync(session);
        hist.Should().HaveCount(2);
        hist[0].Role.Should().Be("user");
        hist[1].Content.Should().Be("Hello");
    }

    [Fact]
    public async Task GetPreferences_SameUser_ShouldReturnSameInstance()
    {
        var first = await _service.GetPreferencesAsync("same-user");
        var second = await _service.GetPreferencesAsync("same-user");
        first.Should().BeSameAs(second);
    }
}
