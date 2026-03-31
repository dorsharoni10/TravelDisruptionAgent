using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Domain.Interfaces;
using TravelDisruptionAgent.Domain.Models;

namespace TravelDisruptionAgent.Infrastructure.Providers;

public class MockWeatherProvider : IWeatherProvider
{
    private readonly ILogger<MockWeatherProvider> _logger;

    private static readonly Dictionary<string, WeatherScenario> ScenariosByLocation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JFK"] = new("New York (JFK)", 5.0, "Heavy Snow", 45.0, 15.0, 2, true, "Winter storm warning — heavy snowfall expected"),
        ["LHR"] = new("London Heathrow", 12.0, "Overcast", 25.0, 3.0, 8, false, null),
        ["ORD"] = new("Chicago O'Hare", -2.0, "Blizzard", 70.0, 30.0, 1, true, "Blizzard warning — airport operations severely impacted"),
        ["LAX"] = new("Los Angeles", 24.0, "Clear", 10.0, 0.0, 15, false, null),
        ["TLV"] = new("Tel Aviv (TLV)", 28.0, "Sunny", 12.0, 0.0, 20, false, null),
        ["FRA"] = new("Frankfurt", 8.0, "Fog", 20.0, 1.0, 3, false, null),
        ["DXB"] = new("Dubai", 38.0, "Clear", 15.0, 0.0, 20, false, null),
        ["NRT"] = new("Tokyo Narita", 15.0, "Typhoon Warning", 90.0, 50.0, 1, true, "Typhoon approaching — all flights may be cancelled"),
        ["CDG"] = new("Paris CDG", 10.0, "Rain", 30.0, 8.0, 6, false, null),
        ["SFO"] = new("San Francisco", 16.0, "Fog", 18.0, 0.5, 2, false, null),
    };

    public MockWeatherProvider(ILogger<MockWeatherProvider> logger)
    {
        _logger = logger;
    }

    public Task<WeatherInfo> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] Weather lookup for {Location}", location);

        var scenario = ScenariosByLocation.GetValueOrDefault(location.ToUpperInvariant())
                       ?? new WeatherScenario(location, 22.0, "Partly Cloudy", 15.0, 0.0, 10, false, null);

        var weather = new WeatherInfo(
            Location: scenario.LocationName,
            TemperatureCelsius: scenario.TempC,
            Condition: scenario.Condition,
            WindSpeedKmh: scenario.WindKph,
            PrecipitationMm: scenario.PrecipMm,
            VisibilityKm: scenario.VisibilityKm,
            HasSevereAlert: scenario.HasAlert,
            AlertDescription: scenario.AlertDescription,
            ObservedAt: DateTime.UtcNow
        );

        return Task.FromResult(weather);
    }

    private record WeatherScenario(
        string LocationName, double TempC, string Condition,
        double WindKph, double PrecipMm, int VisibilityKm,
        bool HasAlert, string? AlertDescription);
}
