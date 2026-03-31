using System.ComponentModel;
using Microsoft.SemanticKernel;
using TravelDisruptionAgent.Domain.Interfaces;

namespace TravelDisruptionAgent.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// Semantic Kernel plugin exposing travel-related tools to the AI agent.
/// </summary>
public class TravelToolsPlugin
{
    private readonly IWeatherProvider _weatherProvider;
    private readonly IFlightProvider _flightProvider;

    public TravelToolsPlugin(IWeatherProvider weatherProvider, IFlightProvider flightProvider)
    {
        _weatherProvider = weatherProvider;
        _flightProvider = flightProvider;
    }

    [KernelFunction("get_weather")]
    [Description("Get current weather conditions for a location")]
    public async Task<string> GetWeatherAsync(
        [Description("City name or airport code")] string location)
    {
        var weather = await _weatherProvider.GetCurrentWeatherAsync(location);
        return $"Weather in {weather.Location}: {weather.Condition}, {weather.TemperatureCelsius}°C, " +
               $"Wind: {weather.WindSpeedKmh} km/h" +
               (weather.HasSevereAlert ? $" ⚠️ ALERT: {weather.AlertDescription}" : "");
    }

    [KernelFunction("get_flight_status")]
    [Description("Get the current status of a specific flight")]
    public async Task<string> GetFlightStatusAsync(
        [Description("Flight number (e.g., UA123)")] string flightNumber,
        [Description("Date of the flight in ISO format")] string date)
    {
        var parsedDate = DateTime.Parse(date);
        var flight = await _flightProvider.GetFlightStatusAsync(flightNumber, parsedDate);

        if (flight is null)
            return $"No information found for flight {flightNumber} on {date}.";

        return $"Flight {flight.FlightNumber} ({flight.Airline}): {flight.Status}" +
               $"\n{flight.DepartureAirport} → {flight.ArrivalAirport}" +
               $"\nScheduled: {flight.ScheduledDeparture:HH:mm} → {flight.ScheduledArrival:HH:mm}" +
               (flight.DelayMinutes.HasValue ? $"\nDelay: {flight.DelayMinutes} minutes" : "") +
               (flight.Gate is not null ? $"\nGate: {flight.Gate}, Terminal: {flight.Terminal}" : "");
    }

    [KernelFunction("search_alternative_flights")]
    [Description("Search for alternative flights between two airports")]
    public async Task<string> SearchFlightsAsync(
        [Description("Origin airport code")] string origin,
        [Description("Destination airport code")] string destination,
        [Description("Date in ISO format")] string date)
    {
        var parsedDate = DateTime.Parse(date);
        var flights = await _flightProvider.SearchFlightsAsync(origin, destination, parsedDate);

        if (flights.Count == 0)
            return $"No flights found from {origin} to {destination} on {date}.";

        return string.Join("\n\n", flights.Select(f =>
            $"✈️ {f.FlightNumber} ({f.Airline}): {f.Status}" +
            $"\nDeparts: {f.ScheduledDeparture:HH:mm} | Arrives: {f.ScheduledArrival:HH:mm}" +
            $"\nGate: {f.Gate ?? "TBD"} | Terminal: {f.Terminal ?? "TBD"}"));
    }
}
