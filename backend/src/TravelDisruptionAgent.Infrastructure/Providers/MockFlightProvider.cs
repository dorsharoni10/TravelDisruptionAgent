using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Domain.Interfaces;
using TravelDisruptionAgent.Domain.Models;

namespace TravelDisruptionAgent.Infrastructure.Providers;

public class MockFlightProvider : IFlightProvider
{
    private readonly ILogger<MockFlightProvider> _logger;

    public MockFlightProvider(ILogger<MockFlightProvider> logger)
    {
        _logger = logger;
    }

    public Task<FlightStatus?> GetFlightStatusAsync(
        string flightNumber, DateTime date, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] Flight lookup: {FlightNumber} on {Date:yyyy-MM-dd}", flightNumber, date);

        var upper = flightNumber.ToUpperInvariant();

        // Scenario: flight not found (for testing fallback)
        if (upper is "XX999" or "FAKE99" or "ZZ000")
            return Task.FromResult<FlightStatus?>(null);

        // Scenario mapping for different demo scenarios
        var flight = upper switch
        {
            "UA234" => new FlightStatus(
                "UA234", "United Airlines", "JFK", "LHR",
                date.Date.AddHours(19), date.Date.AddHours(19).AddMinutes(45),
                date.Date.AddDays(1).AddHours(7), null,
                "Delayed", 45, "B22", "7"),

            "LH789" => new FlightStatus(
                "LH789", "Lufthansa", "FRA", "JFK",
                date.Date.AddHours(10), null,
                date.Date.AddHours(14), null,
                "Cancelled", null, null, "1"),

            "BA456" => new FlightStatus(
                "BA456", "British Airways", "LHR", "CDG",
                date.Date.AddHours(8, 30), date.Date.AddHours(8, 30),
                date.Date.AddHours(10, 45), date.Date.AddHours(10, 40),
                "Landed", 0, "A14", "5"),

            "EK101" => new FlightStatus(
                "EK101", "Emirates", "DXB", "LHR",
                date.Date.AddHours(7, 15), null,
                date.Date.AddHours(12), null,
                "Scheduled", null, "C8", "3"),

            "AA100" => new FlightStatus(
                "AA100", "American Airlines", "JFK", "LHR",
                date.Date.AddHours(22), date.Date.AddDays(1).AddHours(1),
                date.Date.AddDays(1).AddHours(10), null,
                "Delayed", 180, "D12", "8"),

            // Default: delayed flight with common route
            _ => new FlightStatus(
                upper, "Airline", "TLV", "JFK",
                date.Date.AddHours(10), date.Date.AddHours(12),
                date.Date.AddHours(20), null,
                "Delayed", 120, "B12", "3")
        };

        return Task.FromResult<FlightStatus?>(flight);
    }

    public Task<IReadOnlyList<FlightStatus>> SearchFlightsAsync(
        string origin, string destination, DateTime date, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] Route search: {Origin}→{Destination} on {Date:yyyy-MM-dd}", origin, destination, date);

        IReadOnlyList<FlightStatus> flights =
        [
            new FlightStatus($"MA{100 + origin.GetHashCode() % 100:D3}", "MockAir", origin, destination,
                date.Date.AddHours(6, 30), null, date.Date.AddHours(10, 45), null,
                "Scheduled", null, "A1", "1"),

            new FlightStatus($"MA{200 + destination.GetHashCode() % 100:D3}", "MockAir", origin, destination,
                date.Date.AddHours(12), null, date.Date.AddHours(16, 15), null,
                "Scheduled", null, "B5", "2"),

            new FlightStatus($"MA{300 + (origin + destination).GetHashCode() % 100:D3}", "MockAir", origin, destination,
                date.Date.AddHours(18, 30), null, date.Date.AddHours(22, 45), null,
                "Scheduled", null, "C9", "1"),
        ];

        return Task.FromResult(flights);
    }
}

file static class DateTimeHelper
{
    public static DateTime AddHours(this DateTime dt, int hours, int minutes) =>
        dt.AddHours(hours).AddMinutes(minutes);
}
