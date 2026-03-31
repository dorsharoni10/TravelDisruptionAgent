using System.Text.RegularExpressions;
using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Validation;

/// <summary>Extra rules not covered by data annotations (per-item list checks, normalized risk).</summary>
public static class UserPreferencesValidator
{
    private static readonly Regex AirportCodeRegex = new(
        @"^[A-Za-z0-9]{3,4}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> RiskValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "safe", "moderate", "aggressive"
    };

    /// <summary>Returns field-keyed validation errors suitable for <c>ValidationProblemDetails</c>.</summary>
    public static IReadOnlyDictionary<string, List<string>> GetErrors(UserPreferencesDto dto)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = [];
                errors[field] = list;
            }

            list.Add(message);
        }

        if (!RiskValues.Contains((dto.RiskTolerance ?? "").Trim()))
            Add(nameof(UserPreferencesDto.RiskTolerance), "Must be safe, moderate, or aggressive.");

        var home = (dto.HomeAirport ?? "").Trim();
        if (home.Length > 0 && !AirportCodeRegex.IsMatch(home))
            Add(nameof(UserPreferencesDto.HomeAirport),
                $"Must be empty or a 3–{PreferencesInputLimits.MaxAirportCodeLength}-character airport code (letters/digits only).");

        var airports = dto.PreferredAirports ?? [];
        if (airports.Count > PreferencesInputLimits.MaxPreferredAirportsCount)
        {
            Add(nameof(UserPreferencesDto.PreferredAirports),
                $"At most {PreferencesInputLimits.MaxPreferredAirportsCount} airport codes allowed.");
        }

        for (var i = 0; i < airports.Count; i++)
        {
            var code = airports[i]?.Trim() ?? "";
            if (code.Length == 0)
            {
                Add(nameof(UserPreferencesDto.PreferredAirports), $"Entry {i + 1} must not be empty.");
                continue;
            }

            if (code.Length > PreferencesInputLimits.MaxAirportCodeLength ||
                !AirportCodeRegex.IsMatch(code))
            {
                Add(nameof(UserPreferencesDto.PreferredAirports),
                    $"Entry {i + 1} must be a 3–{PreferencesInputLimits.MaxAirportCodeLength}-character airport code (letters/digits only).");
            }
        }

        return errors;
    }

    /// <summary>Canonical value for persistence and prompts (after validation).</summary>
    public static string NormalizeRiskTolerance(string riskTolerance) =>
        (riskTolerance ?? "").Trim().ToLowerInvariant();
}
