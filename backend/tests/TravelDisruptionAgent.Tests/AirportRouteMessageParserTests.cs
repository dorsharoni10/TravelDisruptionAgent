using FluentAssertions;
using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class AirportRouteMessageParserTests
{
    [Fact]
    public void From_city_to_city_resolves_to_IATA_pair()
    {
        var codes = AirportRouteMessageParser.ExtractLocationCodes(
            "My flight from NYC to London was cancelled. What are my options?");
        codes.Should().Equal("NYC", "LHR");
    }

    [Fact]
    public void Bare_IATA_codes_when_no_from_to_phrase()
    {
        var codes = AirportRouteMessageParser.ExtractLocationCodes("Looking at TLV and ATH next week");
        codes.Should().ContainInOrder("TLV", "ATH");
    }

    [Fact]
    public void From_to_strips_trailing_tomorrow_from_destination()
    {
        var codes = AirportRouteMessageParser.ExtractLocationCodes(
            "I need a flight from JFK to CDG tomorrow");
        codes.Should().Equal("JFK", "CDG");
    }
}
