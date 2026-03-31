using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Domain.Interfaces;
using TravelDisruptionAgent.Domain.Models;

namespace TravelDisruptionAgent.Infrastructure.Providers;

public class WeatherApiProvider : IWeatherProvider
{
    private readonly HttpClient _httpClient;
    private readonly WeatherApiOptions _options;
    private readonly ILogger<WeatherApiProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WeatherApiProvider(
        HttpClient httpClient,
        IOptions<WeatherApiOptions> options,
        ILogger<WeatherApiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WeatherInfo> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Real] Fetching weather for {Location} from WeatherAPI", location);

        var url = $"current.json?key={_options.ApiKey}&q={Uri.EscapeDataString(location)}&aqi=no";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<WeatherApiCurrentResponse>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Failed to parse WeatherAPI response");

        return new WeatherInfo(
            Location: data.Location?.Name ?? location,
            TemperatureCelsius: data.Current?.TempC ?? 0,
            Condition: data.Current?.Condition?.Text ?? "Unknown",
            WindSpeedKmh: data.Current?.WindKph ?? 0,
            PrecipitationMm: data.Current?.PrecipMm ?? 0,
            VisibilityKm: (int)(data.Current?.VisKm ?? 10),
            HasSevereAlert: false,
            AlertDescription: null,
            ObservedAt: DateTime.UtcNow
        );
    }

    #region API Response Models

    private class WeatherApiCurrentResponse
    {
        public WeatherLocation? Location { get; set; }
        public WeatherCurrent? Current { get; set; }
    }

    private class WeatherLocation
    {
        public string? Name { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
    }

    private class WeatherCurrent
    {
        [JsonPropertyName("temp_c")]
        public double TempC { get; set; }

        public WeatherCondition? Condition { get; set; }

        [JsonPropertyName("wind_kph")]
        public double WindKph { get; set; }

        [JsonPropertyName("precip_mm")]
        public double PrecipMm { get; set; }

        [JsonPropertyName("vis_km")]
        public double VisKm { get; set; }
    }

    private class WeatherCondition
    {
        public string? Text { get; set; }
    }

    #endregion
}
