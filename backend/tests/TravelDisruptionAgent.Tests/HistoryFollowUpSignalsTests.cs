using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class HistoryFollowUpSignalsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("What about the alternative flight you suggested?", true)]
    [InlineData("What was the arrival time of the alternative?", true)]
    [InlineData("You mentioned a replacement earlier — confirm departure time", true)]
    [InlineData("You found TLV-ATH options; what time is departure?", true)]
    [InlineData("Is that the same flight as before?", true)]
    [InlineData("Book a hotel in Paris", false)]
    public void LikelyReferencesPriorFlightDetail_matches_expected(string? message, bool expected)
    {
        Assert.Equal(expected, HistoryFollowUpSignals.LikelyReferencesPriorFlightDetail(message ?? ""));
    }
}
