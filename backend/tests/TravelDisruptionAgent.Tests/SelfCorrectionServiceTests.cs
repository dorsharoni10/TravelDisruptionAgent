using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class SelfCorrectionServiceTests
{
    private readonly SelfCorrectionService _service;

    public SelfCorrectionServiceTests()
    {
        var coordinator = new Mock<IToolExecutionCoordinator>();
        _service = new SelfCorrectionService(coordinator.Object, NullLogger<SelfCorrectionService>.Instance);
    }

    [Fact]
    public async Task Review_AllToolsSucceeded_ShouldMaintainConfidence()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByNumber", "UA234", "Flight found", true, "mock:flights", 50),
            new("WeatherLookup", "JFK", "Clear skies", true, "mock:weather", 30)
        };

        var result = await _service.ReviewAndCorrectAsync("test", tools, "Recommendation", 0.85);
        result.AdjustedConfidence.Should().BeGreaterOrEqualTo(0.7);
        result.FinalRecommendation.Should().Contain("Recommendation");
    }

    [Fact]
    public async Task Review_FlightNotFound_ShouldAddCorrectionStep()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByNumber", "XX999", "Not found", false, "mock:flights", 50,
                ErrorMessage: "Flight not found")
        };

        var result = await _service.ReviewAndCorrectAsync("Flight XX999", tools, "Recommendation", 0.8);
        result.Steps.Should().NotBeEmpty();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Review_WeatherFailed_ShouldLowerConfidence()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("WeatherLookup", "JFK", "Failed", false, "mock:weather", 50, ErrorMessage: "Timeout")
        };

        var result = await _service.ReviewAndCorrectAsync("weather at JFK", tools, "Recommendation", 0.8);
        result.AdjustedConfidence.Should().BeLessThan(0.8);
        result.Steps.Should().Contain(s => s.Issue.Contains("Weather"));
    }

    [Fact]
    public async Task Review_FallbackUsed_ShouldAddWarning()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("WeatherLookup", "JFK", "Simulated data", true, "mock:weather (fallback)", 50,
                Warning: "Real-time weather unavailable")
        };

        var result = await _service.ReviewAndCorrectAsync("test", tools, "Recommendation", 0.8);
        result.Steps.Should().Contain(s => s.Issue.Contains("Real-time"));
        result.AdjustedConfidence.Should().BeLessThan(0.8);
    }

    [Fact]
    public async Task Review_NoToolData_ShouldHaveLowConfidence()
    {
        var result = await _service.ReviewAndCorrectAsync("test", [], "General advice", 0.5);
        result.AdjustedConfidence.Should().BeLessOrEqualTo(0.3);
        result.Warnings.Should().Contain(w => w.Contains("No real-time data"));
    }

    [Fact]
    public async Task Review_SevereWeatherOnTimeFlights_ShouldDetectInconsistency()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("WeatherLookup", "JFK", "Blizzard\nDisruption risk: HIGH", true, "mock:weather", 30),
            new("FlightLookupByNumber", "UA234", "Status: Scheduled\nGate: B12", true, "mock:flights", 50)
        };

        var result = await _service.ReviewAndCorrectAsync("test", tools, "All looks fine", 0.9);
        result.Steps.Should().Contain(s => s.Issue.Contains("Inconsistency"));
        result.AdjustedConfidence.Should().BeLessThan(0.9);
    }
}
