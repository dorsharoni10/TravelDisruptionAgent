using FluentAssertions;
using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class UserToolRequestSignalsTests
{
    [Fact]
    public void Cancelled_flight_plus_what_are_my_options_implies_route_or_alternatives()
    {
        UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(
                "My flight from NYC to London was cancelled. What are my options?")
            .Should().BeTrue();
    }

    [Fact]
    public void Options_without_disruption_does_not_trigger_route_signal()
    {
        UserToolRequestSignals.ExplicitlyAsksForRouteOrAlternatives(
                "What are my options for lounge access?")
            .Should().BeFalse();
    }
}
