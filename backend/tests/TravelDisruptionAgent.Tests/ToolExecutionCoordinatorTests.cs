using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TravelDisruptionAgent.Infrastructure.Providers;
using TravelDisruptionAgent.Infrastructure.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class ToolExecutionCoordinatorTests
{
    private readonly ToolExecutionCoordinator _coordinator;

    public ToolExecutionCoordinatorTests()
    {
        var mockWeather = new MockWeatherProvider(NullLogger<MockWeatherProvider>.Instance);
        var mockFlight = new MockFlightProvider(NullLogger<MockFlightProvider>.Instance);

        _coordinator = new ToolExecutionCoordinator(
            mockWeather, mockFlight, mockWeather, mockFlight,
            NullLogger<ToolExecutionCoordinator>.Instance);
    }

    [Fact]
    public async Task LookupFlightByNumber_KnownFlight_ShouldReturnSuccess()
    {
        var result = await _coordinator.LookupFlightByNumberAsync("UA234", DateTime.Today);
        result.Success.Should().BeTrue();
        result.ToolName.Should().Be("FlightLookupByNumber");
        result.DataSource.Should().Be("mock:flights");
        result.Output.Should().Contain("UA234");
        result.Output.Should().Contain("Delayed");
    }

    [Fact]
    public async Task LookupFlightByNumber_UnknownFlight_ShouldReturnNotFound()
    {
        var result = await _coordinator.LookupFlightByNumberAsync("XX999", DateTime.Today);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Flight not found");
    }

    [Fact]
    public async Task LookupFlightByRoute_ShouldReturnMultipleFlights()
    {
        var result = await _coordinator.LookupFlightByRouteAsync("JFK", "LHR", DateTime.Today);
        result.Success.Should().BeTrue();
        result.ToolName.Should().Be("FlightLookupByRoute");
        result.Output.Should().Contain("Found");
    }

    [Fact]
    public async Task LookupWeather_KnownLocation_ShouldReturnWeather()
    {
        var result = await _coordinator.LookupWeatherAsync("JFK");
        result.Success.Should().BeTrue();
        result.ToolName.Should().Be("WeatherLookup");
        result.DataSource.Should().Be("mock:weather");
        result.Output.Should().Contain("Heavy Snow");
        result.Output.Should().Contain("HIGH");
    }

    [Fact]
    public async Task LookupWeather_SevereWeather_ShouldShowAlert()
    {
        var result = await _coordinator.LookupWeatherAsync("ORD");
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Blizzard");
        result.Output.Should().Contain("SEVERE ALERT");
    }

    [Fact]
    public async Task LookupFlightByNumber_CancelledFlight_ShouldReturnCancelled()
    {
        var result = await _coordinator.LookupFlightByNumberAsync("LH789", DateTime.Today);
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Cancelled");
    }
}
