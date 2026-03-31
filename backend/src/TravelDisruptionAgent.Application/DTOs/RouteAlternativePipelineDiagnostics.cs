namespace TravelDisruptionAgent.Application.DTOs;

/// <summary>Counts produced while filtering FlightLookupByRoute rows into <see cref="VerifiedContext.CleanAlternatives"/>.</summary>
public sealed record RouteAlternativePipelineDiagnostics(
    int NormalizedInputRows,
    int ExcludedAsUserPrimaryFlightNumber,
    int PoolAfterQualityFilters,
    IReadOnlyList<RouteAlternativeTierDiagnostics> Tiers,
    int FinalAlternativeCount,
    string? SampleDepartureTimesUtc);

public sealed record RouteAlternativeTierDiagnostics(
    int HorizonHours,
    int CountInTimeWindow,
    int CountAfterCodeshareDedup);
