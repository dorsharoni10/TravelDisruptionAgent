namespace TravelDisruptionAgent.Domain.Models;

public record WeatherInfo(
    string Location,
    double TemperatureCelsius,
    string Condition,
    double WindSpeedKmh,
    double PrecipitationMm,
    int VisibilityKm,
    bool HasSevereAlert,
    string? AlertDescription,
    DateTime ObservedAt
);
