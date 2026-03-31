using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Domain.Interfaces;
using TravelDisruptionAgent.Domain.Models;

namespace TravelDisruptionAgent.Infrastructure.Providers;

public class AviationStackFlightProvider : IFlightProvider
{
    private readonly HttpClient _httpClient;
    private readonly AviationStackOptions _options;
    private readonly ILogger<AviationStackFlightProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AviationStackFlightProvider(
        HttpClient httpClient,
        IOptions<AviationStackOptions> options,
        ILogger<AviationStackFlightProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FlightStatus?> GetFlightStatusAsync(
        string flightNumber, DateTime date, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Real] Looking up flight {FlightNumber} via AviationStack", flightNumber);

        var url = $"flights?access_key={_options.ApiKey}&flight_iata={Uri.EscapeDataString(flightNumber)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<AviationStackResponse>(json, JsonOptions);

        var flight = data?.Data?.FirstOrDefault();
        if (flight is null) return null;

        return MapToFlightStatus(flight);
    }

    public async Task<IReadOnlyList<FlightStatus>> SearchFlightsAsync(
        string origin, string destination, DateTime date, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Real] Searching flights {Origin}→{Destination} via AviationStack", origin, destination);

        var dateStr = date.ToString("yyyy-MM-dd");
        var url = $"flights?access_key={_options.ApiKey}" +
                  $"&dep_iata={Uri.EscapeDataString(origin)}" +
                  $"&arr_iata={Uri.EscapeDataString(destination)}";
        if (_options.IncludeFlightDateOnRouteSearch)
            url += $"&flight_date={Uri.EscapeDataString(dateStr)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<AviationStackResponse>(json, JsonOptions);

        if (data?.Data is null or { Count: 0 })
        {
            _logger.LogInformation(
                "[Real] Route search {Origin}→{Destination} flight_date={Date}: 0 rows from API",
                origin, destination, dateStr);
            return [];
        }

        _logger.LogInformation(
            "[Real] Route search {Origin}→{Destination} flight_date={Date}: {Count} row(s) from API (before local mapping)",
            origin, destination, dateStr, data.Data.Count);

        return data.Data
            .Select(MapToFlightStatus)
            .Where(f => f is not null)
            .Cast<FlightStatus>()
            .ToList();
    }

    private static FlightStatus? MapToFlightStatus(AviationStackFlight flight)
    {
        var flightIata = flight.Flight?.Iata;
        if (string.IsNullOrEmpty(flightIata)) return null;

        return new FlightStatus(
            FlightNumber: flightIata,
            Airline: flight.Airline?.Name ?? "Unknown",
            DepartureAirport: flight.Departure?.Iata ?? "???",
            ArrivalAirport: flight.Arrival?.Iata ?? "???",
            ScheduledDeparture: ParseDateTimeSafe(flight.Departure?.Scheduled),
            ActualDeparture: ParseDateTimeNullable(flight.Departure?.Actual),
            ScheduledArrival: ParseDateTimeSafe(flight.Arrival?.Scheduled),
            ActualArrival: ParseDateTimeNullable(flight.Arrival?.Actual),
            Status: MapStatus(flight.FlightStatus),
            DelayMinutes: flight.Departure?.Delay,
            Gate: flight.Departure?.Gate,
            Terminal: flight.Departure?.Terminal
        );
    }

    private static string MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "scheduled" => "Scheduled",
        "active" => "In Flight",
        "landed" => "Landed",
        "cancelled" => "Cancelled",
        "incident" => "Incident",
        "diverted" => "Diverted",
        _ => status ?? "Unknown"
    };

    private static DateTime ParseDateTimeSafe(string? dateStr) =>
        TryParseApiDateTime(dateStr, out var dt) ? dt : DateTime.UtcNow;

    private static DateTime? ParseDateTimeNullable(string? dateStr) =>
        string.IsNullOrEmpty(dateStr) ? null : TryParseApiDateTime(dateStr, out var dt) ? dt : null;

    /// <summary>AviationStack often returns ISO-8601 without offset; treat as UTC for consistent filtering.</summary>
    private static bool TryParseApiDateTime(string? dateStr, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;
        if (DateTime.TryParse(
                dateStr,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc))
            return true;
        return false;
    }

    #region API Response Models

    private class AviationStackResponse
    {
        public List<AviationStackFlight>? Data { get; set; }
    }

    private class AviationStackFlight
    {
        public string? FlightDate { get; set; }
        public string? FlightStatus { get; set; }
        public AviationEndpoint? Departure { get; set; }
        public AviationEndpoint? Arrival { get; set; }
        public AviationAirline? Airline { get; set; }
        public AviationFlightInfo? Flight { get; set; }
    }

    private class AviationEndpoint
    {
        public string? Airport { get; set; }
        public string? Iata { get; set; }
        public string? Scheduled { get; set; }
        public string? Actual { get; set; }
        public int? Delay { get; set; }
        public string? Terminal { get; set; }
        public string? Gate { get; set; }
    }

    private class AviationAirline
    {
        public string? Name { get; set; }
        public string? Iata { get; set; }
    }

    private class AviationFlightInfo
    {
        public string? Number { get; set; }
        public string? Iata { get; set; }
    }

    #endregion
}
