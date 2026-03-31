using TravelDisruptionAgent.Domain.Models;

namespace TravelDisruptionAgent.Domain.Interfaces;

public interface IWeatherProvider
{
    Task<WeatherInfo> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken = default);
}
