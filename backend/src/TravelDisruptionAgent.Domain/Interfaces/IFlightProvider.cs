using TravelDisruptionAgent.Domain.Models;

namespace TravelDisruptionAgent.Domain.Interfaces;

public interface IFlightProvider
{
    Task<FlightStatus?> GetFlightStatusAsync(string flightNumber, DateTime date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FlightStatus>> SearchFlightsAsync(string origin, string destination, DateTime date, CancellationToken cancellationToken = default);
}
