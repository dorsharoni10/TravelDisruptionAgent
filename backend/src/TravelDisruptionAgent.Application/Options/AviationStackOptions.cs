namespace TravelDisruptionAgent.Application.Options;

public class AviationStackOptions
{
    public const string SectionName = "AviationStack";

    /// <summary>When false and <see cref="ApiKey"/> is set, calls AviationStack (real external tool).</summary>
    public bool UseMock { get; set; } = false;
    public string BaseUrl { get; set; } = "https://api.aviationstack.com/v1";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, route search appends flight_date (YYYY-MM-DD). Many free/low AviationStack tiers return 403 if this is sent; enable only on plans that allow it.
    /// </summary>
    public bool IncludeFlightDateOnRouteSearch { get; set; } = false;
}
