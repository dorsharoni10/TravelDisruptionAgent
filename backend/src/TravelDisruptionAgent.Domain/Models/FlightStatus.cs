namespace TravelDisruptionAgent.Domain.Models;

public record FlightStatus(
    string FlightNumber,
    string Airline,
    string DepartureAirport,
    string ArrivalAirport,
    DateTime ScheduledDeparture,
    DateTime? ActualDeparture,
    DateTime ScheduledArrival,
    DateTime? ActualArrival,
    string Status,
    int? DelayMinutes,
    string? Gate,
    string? Terminal
);
