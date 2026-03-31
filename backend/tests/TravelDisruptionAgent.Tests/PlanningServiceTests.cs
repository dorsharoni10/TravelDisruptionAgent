using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class PlanningServiceTests
{
    private readonly PlanningService _service;

    public PlanningServiceTests()
    {
        var kernelFactory = new Mock<IKernelFactory>();
        kernelFactory.Setup(k => k.IsConfigured).Returns(false);
        _service = new PlanningService(kernelFactory.Object, NullLogger<PlanningService>.Instance);
    }

    [Fact]
    public async Task CreatePlan_FlightCancellation_ShouldIncludeRebookingSteps()
    {
        var plan = await _service.CreatePlanAsync(
            "My flight was cancelled — I need alternative flights on the same route and rebooking options.",
            "flight_cancellation",
            false);
        plan.Goal.Should().NotBeNullOrEmpty();
        plan.Steps.Should().HaveCountGreaterOrEqualTo(3);
        plan.Steps.Should().Contain(s => s.Contains("flight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlan_WeatherDisruption_ShouldIncludeWeatherSteps()
    {
        var plan = await _service.CreatePlanAsync("Snowstorm at JFK", "weather_disruption", false);
        plan.Steps.Should().Contain(s => s.Contains("weather", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlan_MissedConnection_ShouldIncludeConnectionSteps()
    {
        var plan = await _service.CreatePlanAsync(
            "I missed my connection in Frankfurt — search for next available connecting flights to rebook.",
            "missed_connection",
            false);
        plan.Steps.Should().HaveCountGreaterOrEqualTo(3);
        plan.Steps.Should().Contain(s => s.Contains("connect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlan_UnknownIntent_ShouldReturnFallbackPlan()
    {
        var plan = await _service.CreatePlanAsync("Something happened", "unknown_intent", false);
        plan.Steps.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Theory]
    [InlineData("flight_cancellation")]
    [InlineData("flight_delay")]
    [InlineData("weather_disruption")]
    [InlineData("missed_connection")]
    [InlineData("rebooking_request")]
    [InlineData("baggage_issue")]
    public async Task CreatePlan_AllIntents_ShouldReturnValidPlan(string intent)
    {
        var plan = await _service.CreatePlanAsync("Test message", intent, false);
        plan.Goal.Should().NotBeNullOrEmpty();
        plan.Steps.Should().NotBeEmpty();
    }
}
