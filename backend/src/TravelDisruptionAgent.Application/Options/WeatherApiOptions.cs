namespace TravelDisruptionAgent.Application.Options;

public class WeatherApiOptions
{
    public const string SectionName = "WeatherApi";

    /// <summary>When false and <see cref="ApiKey"/> is set, calls WeatherAPI.com (real external tool).</summary>
    public bool UseMock { get; set; } = false;
    public string BaseUrl { get; set; } = "https://api.weatherapi.com/v1";
    public string ApiKey { get; set; } = string.Empty;
}
