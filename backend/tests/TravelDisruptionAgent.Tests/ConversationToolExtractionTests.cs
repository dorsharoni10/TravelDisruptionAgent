using FluentAssertions;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class ConversationToolExtractionTests
{
    [Fact]
    public void Arrival_time_for_alternative_is_follow_up()
    {
        ConversationToolExtraction.LooksLikeFlightRelatedFollowUp(
                "what is the arrival time for the alternative flight you found?")
            .Should().BeTrue();
    }

    [Fact]
    public void Thanks_is_not_follow_up()
    {
        ConversationToolExtraction.LooksLikeFlightRelatedFollowUp("thanks!")
            .Should().BeFalse();
    }

    [Fact]
    public void Merge_appends_last_assistant_when_follow_up_lacks_codes()
    {
        var prior = new List<ChatMessage>
        {
            new("user", "help with IZ1220 TLV ATH", DateTime.UtcNow),
            new("assistant",
                "You may rebook on flight MA286 from TLV to ATH on 2026-03-30 departing 18:30.",
                DateTime.UtcNow)
        };

        var merged = ConversationToolExtraction.BuildToolSignalText(
            "what time does it arrive?",
            prior,
            currentMessageHasFlightNumber: false,
            currentMessageHasTwoLocationCodes: false);

        merged.Should().Contain("prior_assistant_context");
        merged.Should().Contain("MA286");
        merged.Should().Contain("TLV");
    }

    [Fact]
    public void No_merge_when_current_message_already_has_full_codes()
    {
        var prior = new List<ChatMessage>
        {
            new("assistant", "Old context IZ999", DateTime.UtcNow)
        };

        var merged = ConversationToolExtraction.BuildToolSignalText(
            "IZ1220 from TLV to JFK status",
            prior,
            currentMessageHasFlightNumber: true,
            currentMessageHasTwoLocationCodes: true);

        merged.Should().Be("IZ1220 from TLV to JFK status");
    }
}
