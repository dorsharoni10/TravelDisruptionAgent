using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Domain.Interfaces;
using TravelDisruptionAgent.Domain.Models;
using TravelDisruptionAgent.Infrastructure.Providers;

namespace TravelDisruptionAgent.Infrastructure.Services;

public class ToolExecutionCoordinator : IToolExecutionCoordinator
{
    private readonly IWeatherProvider _primaryWeather;
    private readonly IFlightProvider _primaryFlight;
    private readonly MockWeatherProvider _mockWeather;
    private readonly MockFlightProvider _mockFlight;
    private readonly ILogger<ToolExecutionCoordinator> _logger;

    private readonly bool _primaryWeatherIsMock;
    private readonly bool _primaryFlightIsMock;

    public ToolExecutionCoordinator(
        IWeatherProvider primaryWeather,
        IFlightProvider primaryFlight,
        MockWeatherProvider mockWeather,
        MockFlightProvider mockFlight,
        ILogger<ToolExecutionCoordinator> logger)
    {
        _primaryWeather = primaryWeather;
        _primaryFlight = primaryFlight;
        _mockWeather = mockWeather;
        _mockFlight = mockFlight;
        _logger = logger;

        _primaryWeatherIsMock = primaryWeather is MockWeatherProvider;
        _primaryFlightIsMock = primaryFlight is MockFlightProvider;
    }

    public async Task<ToolExecutionResult> LookupFlightByNumberAsync(
        string flightNumber, DateTime date, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var dataSource = _primaryFlightIsMock ? "mock:flights" : "real:aviationstack";

        try
        {
            var flight = await _primaryFlight.GetFlightStatusAsync(flightNumber, date, cancellationToken);
            sw.Stop();

            if (flight is null)
            {
                return new ToolExecutionResult(
                    "FlightLookupByNumber", flightNumber,
                    $"No flight information found for {flightNumber} on {date:yyyy-MM-dd}.",
                    false, dataSource, sw.ElapsedMilliseconds,
                    ErrorMessage: "Flight not found");
            }

            return new ToolExecutionResult(
                "FlightLookupByNumber", flightNumber,
                FormatFlightStatus(flight), true, dataSource, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!_primaryFlightIsMock)
        {
            _logger.LogWarning(ex, "Real flight provider failed for {FlightNumber}, falling back to mock", flightNumber);
            return await FallbackFlightByNumberAsync(flightNumber, date, sw, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolExecutionResult(
                "FlightLookupByNumber", flightNumber,
                $"Failed to look up flight {flightNumber}.",
                false, dataSource, sw.ElapsedMilliseconds,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<ToolExecutionResult> LookupFlightByRouteAsync(
        string origin, string destination, DateTime date, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var input = $"{origin} → {destination} on {date:yyyy-MM-dd}";
        var dataSource = _primaryFlightIsMock ? "mock:flights" : "real:aviationstack";

        try
        {
            var day1 = await _primaryFlight.SearchFlightsAsync(origin, destination, date, cancellationToken);
            IReadOnlyList<FlightStatus> day2 = [];
            try
            {
                day2 = await _primaryFlight.SearchFlightsAsync(
                    origin, destination, date.AddDays(1), cancellationToken);
            }
            catch (Exception ex) when (!_primaryFlightIsMock)
            {
                _logger.LogWarning(ex,
                    "Route search second-day query failed for {Origin}→{Destination} (day after {Date}); using first day only",
                    origin, destination, date.ToString("yyyy-MM-dd"));
            }

            var flights = day1.Concat(day2)
                .DistinctBy(f => (f.FlightNumber, f.ScheduledDeparture))
                .ToList();
            sw.Stop();

            if (flights.Count == 0)
            {
                return new ToolExecutionResult(
                    "FlightLookupByRoute", input,
                    $"No flights found from {origin} to {destination} on {date:yyyy-MM-dd} (or the following UTC calendar day).",
                    false, dataSource, sw.ElapsedMilliseconds,
                    ErrorMessage: "No flights found on this route");
            }

            var output =
                $"Found {flights.Count} flight(s) (UTC calendar days {date:yyyy-MM-dd} and {date.AddDays(1):yyyy-MM-dd}):\n\n" +
                string.Join("\n---\n", flights.Select(FormatFlightStatus));
            return new ToolExecutionResult(
                "FlightLookupByRoute", input, output, true, dataSource, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!_primaryFlightIsMock)
        {
            _logger.LogWarning(ex, "Real flight provider failed for route {Input}, falling back to mock", input);
            return await FallbackFlightByRouteAsync(origin, destination, date, sw, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolExecutionResult(
                "FlightLookupByRoute", input,
                $"Failed to search flights from {origin} to {destination}.",
                false, dataSource, sw.ElapsedMilliseconds,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<ToolExecutionResult> LookupWeatherAsync(
        string location, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var dataSource = _primaryWeatherIsMock ? "mock:weather" : "real:weatherapi";

        try
        {
            var weather = await _primaryWeather.GetCurrentWeatherAsync(location, cancellationToken);
            sw.Stop();
            return new ToolExecutionResult(
                "WeatherLookup", location,
                FormatWeatherInfo(weather), true, dataSource, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (!_primaryWeatherIsMock)
        {
            _logger.LogWarning(ex, "Real weather provider failed for {Location}, falling back to mock", location);
            return await FallbackWeatherAsync(location, sw, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolExecutionResult(
                "WeatherLookup", location,
                $"Weather data unavailable for {location}.",
                false, dataSource, sw.ElapsedMilliseconds,
                ErrorMessage: ex.Message);
        }
    }

    private async Task<ToolExecutionResult> FallbackFlightByNumberAsync(
        string flightNumber, DateTime date, Stopwatch sw, string originalError, CancellationToken ct)
    {
        try
        {
            var flight = await _mockFlight.GetFlightStatusAsync(flightNumber, date, ct);
            sw.Stop();
            if (flight is null)
                return new ToolExecutionResult("FlightLookupByNumber", flightNumber,
                    $"No flight information found for {flightNumber}.", false,
                    "mock:flights (fallback)", sw.ElapsedMilliseconds, ErrorMessage: "Flight not found");
            return new ToolExecutionResult("FlightLookupByNumber", flightNumber,
                FormatFlightStatus(flight), true, "mock:flights (fallback)", sw.ElapsedMilliseconds,
                Warning: $"Real-time data unavailable ({originalError}). Showing simulated data.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolExecutionResult("FlightLookupByNumber", flightNumber,
                "Flight data completely unavailable.", false, "unavailable", sw.ElapsedMilliseconds,
                ErrorMessage: $"Primary: {originalError} | Fallback: {ex.Message}");
        }
    }

    private async Task<ToolExecutionResult> FallbackFlightByRouteAsync(
        string origin, string destination, DateTime date, Stopwatch sw, string originalError, CancellationToken ct)
    {
        var input = $"{origin} → {destination} on {date:yyyy-MM-dd}";
        try
        {
            var flights = await _mockFlight.SearchFlightsAsync(origin, destination, date, ct);
            sw.Stop();
            if (flights.Count == 0)
                return new ToolExecutionResult("FlightLookupByRoute", input,
                    "No flights found on this route.", false, "mock:flights (fallback)", sw.ElapsedMilliseconds);
            var output = $"Found {flights.Count} flight(s) [simulated]:\n\n" +
                         string.Join("\n---\n", flights.Select(FormatFlightStatus));
            return new ToolExecutionResult("FlightLookupByRoute", input, output, true,
                "mock:flights (fallback)", sw.ElapsedMilliseconds,
                Warning: $"Real-time data unavailable ({originalError}). Showing simulated flights.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolExecutionResult("FlightLookupByRoute", input,
                "Flight data completely unavailable.", false, "unavailable", sw.ElapsedMilliseconds,
                ErrorMessage: $"Primary: {originalError} | Fallback: {ex.Message}");
        }
    }

    private async Task<ToolExecutionResult> FallbackWeatherAsync(
        string location, Stopwatch sw, string originalError, CancellationToken ct)
    {
        try
        {
            var weather = await _mockWeather.GetCurrentWeatherAsync(location, ct);
            sw.Stop();
            return new ToolExecutionResult("WeatherLookup", location,
                FormatWeatherInfo(weather), true, "mock:weather (fallback)", sw.ElapsedMilliseconds,
                Warning: $"Real-time weather unavailable ({originalError}). Showing simulated conditions.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolExecutionResult("WeatherLookup", location,
                "Weather data completely unavailable.", false, "unavailable", sw.ElapsedMilliseconds,
                ErrorMessage: $"Primary: {originalError} | Fallback: {ex.Message}");
        }
    }

    private static string FormatFlightStatus(FlightStatus f) =>
        $"Flight {f.FlightNumber} ({f.Airline}): {f.Status}\n" +
        $"Route: {f.DepartureAirport} → {f.ArrivalAirport}\n" +
        $"Scheduled departure: {f.ScheduledDeparture:yyyy-MM-dd HH:mm}\n" +
        $"Scheduled arrival: {f.ScheduledArrival:yyyy-MM-dd HH:mm}\n" +
        (f.ActualDeparture.HasValue ? $"Actual departure: {f.ActualDeparture:yyyy-MM-dd HH:mm}\n" : "") +
        (f.ActualArrival.HasValue ? $"Actual arrival: {f.ActualArrival:yyyy-MM-dd HH:mm}\n" : "") +
        (f.DelayMinutes.HasValue ? $"Delay: {f.DelayMinutes} minutes\n" : "") +
        (f.Gate is not null ? $"Gate: {f.Gate}, Terminal: {f.Terminal}" : "Gate: TBD");

    private static string FormatWeatherInfo(WeatherInfo w) =>
        $"Location: {w.Location}\n" +
        $"Condition: {w.Condition}\n" +
        $"Temperature: {w.TemperatureCelsius}°C\n" +
        $"Wind: {w.WindSpeedKmh} km/h\n" +
        $"Precipitation: {w.PrecipitationMm} mm\n" +
        $"Visibility: {w.VisibilityKm} km\n" +
        (w.HasSevereAlert ? $"SEVERE ALERT: {w.AlertDescription}\n" : "") +
        $"Disruption risk: {AssessDisruptionRisk(w)}";

    private static string AssessDisruptionRisk(WeatherInfo w)
    {
        if (w.HasSevereAlert) return "HIGH — Expect significant delays or cancellations";
        if (w.WindSpeedKmh > 60 || w.VisibilityKm < 2 || w.PrecipitationMm > 20) return "MODERATE — Possible delays";
        if (w.WindSpeedKmh > 40 || w.VisibilityKm < 5 || w.PrecipitationMm > 10) return "LOW — Minor delays possible";
        return "MINIMAL — Normal operations expected";
    }
}
