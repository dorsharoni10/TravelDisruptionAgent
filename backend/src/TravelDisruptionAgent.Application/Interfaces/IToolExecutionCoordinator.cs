using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IToolExecutionCoordinator
{
    Task<ToolExecutionResult> LookupFlightByNumberAsync(string flightNumber, DateTime date, CancellationToken cancellationToken = default);
    Task<ToolExecutionResult> LookupFlightByRouteAsync(string origin, string destination, DateTime date, CancellationToken cancellationToken = default);
    Task<ToolExecutionResult> LookupWeatherAsync(string location, CancellationToken cancellationToken = default);
}
