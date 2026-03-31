using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class ConversationHistoryRouteFallbackTests
{
    [Fact]
    public void TryAppendSyntheticRouteTool_parses_verified_facts_style_line()
    {
        var prior = new List<ChatMessage>
        {
            new("user", "help", DateTime.UtcNow),
            new("assistant",
                "▸ [ALTERNATIVE — filtered route search] Flight Unknown (Israir Airlines): status is \"Unknown\", route TLV → ATH, scheduled departure 2026-03-30 23:30, scheduled arrival 2026-03-31 01:37",
                DateTime.UtcNow)
        };
        var tools = new List<ToolExecutionResult>();

        var ok = ConversationHistoryRouteFallback.TryAppendSyntheticRouteTool(tools, prior, enableConversationHistoryRouteFallback: true);

        Assert.True(ok);
        Assert.Single(tools);
        Assert.Equal("FlightLookupByRoute", tools[0].ToolName);
        Assert.True(tools[0].Success);
        Assert.Contains("conversation_history_fallback", tools[0].DataSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scheduled arrival: 2026-03-31 01:37", tools[0].Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAppendSyntheticRouteTool_noop_when_route_already_succeeded()
    {
        var prior = new List<ChatMessage>
        {
            new("assistant",
                "route TLV → ATH, scheduled departure 2026-03-30 23:30, scheduled arrival 2026-03-31 01:37 alternative",
                DateTime.UtcNow)
        };
        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByRoute", "TLV,ATH", "Found 3 flights", true, "aviationstack", 1)
        };

        var ok = ConversationHistoryRouteFallback.TryAppendSyntheticRouteTool(tools, prior, enableConversationHistoryRouteFallback: true);

        Assert.False(ok);
        Assert.Single(tools);
    }

    [Fact]
    public void TryAppendSyntheticRouteTool_noop_when_feature_disabled_even_with_matching_prior()
    {
        var prior = new List<ChatMessage>
        {
            new("assistant",
                "▸ [ALTERNATIVE — filtered route search] Flight Unknown (Israir Airlines): status is \"Unknown\", route TLV → ATH, scheduled departure 2026-03-30 23:30, scheduled arrival 2026-03-31 01:37",
                DateTime.UtcNow)
        };
        var tools = new List<ToolExecutionResult>();

        var ok = ConversationHistoryRouteFallback.TryAppendSyntheticRouteTool(tools, prior, enableConversationHistoryRouteFallback: false);

        Assert.False(ok);
        Assert.Empty(tools);
    }
}
