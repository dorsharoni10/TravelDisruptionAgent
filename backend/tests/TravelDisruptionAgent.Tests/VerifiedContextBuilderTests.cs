using FluentAssertions;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class VerifiedContextBuilderTests
{
    [Fact]
    public void ExtractPrimaryFlightNumbers_IncludesToolInput()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByNumber", "IZ1215", "Minimal output", true, "real:test", 0)
        };

        var set = VerifiedContextBuilder.ExtractPrimaryFlightNumbers(tools);

        set.Should().Contain("IZ1215");
    }

    [Fact]
    public void ExtractPrimaryFlightNumbers_ParsesIataFromOutput_NotWholeMatch()
    {
        var output =
            "Flight IZ1215 (Arkia Israeli Airlines): Unknown\n" +
            "Route: TLV → ATH\n" +
            "Scheduled departure: 2026-03-29 23:30\n";

        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByNumber", "IZ1215", output, true, "real:test", 0)
        };

        var set = VerifiedContextBuilder.ExtractPrimaryFlightNumbers(tools);

        set.Should().BeEquivalentTo(new[] { "IZ1215" });
        set.Should().NotContain(s => s.Contains("FlightIZ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractPrimaryFlightNumbers_IgnoresFailedLookups()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByNumber", "XX999", "Not found", false, "real:test", 0, ErrorMessage: "Flight not found")
        };

        var set = VerifiedContextBuilder.ExtractPrimaryFlightNumbers(tools);

        set.Should().BeEmpty();
    }
}
