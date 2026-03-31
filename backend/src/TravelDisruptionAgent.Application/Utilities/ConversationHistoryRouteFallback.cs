using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>
/// When live <see cref="FlightLookupByRoute"/> did not run or failed this request, recover
/// alternative-flight times from a prior assistant turn (same session) so guardrails and
/// self-correction treat them as grounded.
/// </summary>
/// <remarks>
/// Trust boundary: only <see cref="ChatMessage"/> rows with role <c>assistant</c> that were loaded from
/// <see cref="TravelDisruptionAgent.Application.Interfaces.IMemoryService"/> for the session key are considered.
/// User-supplied history in the HTTP body is not used for this path. Disable via
/// <see cref="TravelDisruptionAgent.Application.Options.OrchestrationOptions.EnableConversationHistoryRouteFallback"/> when you must not treat historical prose as tool evidence.
/// </remarks>
public static partial class ConversationHistoryRouteFallback
{
    public const string SyntheticDataSource = "conversation_history_fallback";

    /// <summary>
    /// Appends a synthetic successful route tool result when no successful route lookup exists yet.
    /// </summary>
    /// <param name="enableConversationHistoryRouteFallback">Bound from <c>Orchestration:EnableConversationHistoryRouteFallback</c>.</param>
    public static bool TryAppendSyntheticRouteTool(
        List<ToolExecutionResult> toolResults,
        IReadOnlyList<ChatMessage> priorTurns,
        bool enableConversationHistoryRouteFallback,
        ILogger? logger = null)
    {
        if (!enableConversationHistoryRouteFallback)
        {
            logger?.LogDebug(
                "conversation_history_fallback skipped: EnableConversationHistoryRouteFallback=false. " +
                "Trust boundary when enabled: only IMemoryService messages with role assistant are parsed.");
            return false;
        }

        if (toolResults.Any(t => t.ToolName == "FlightLookupByRoute" && t.Success))
            return false;

        var block = TryBuildSyntheticRouteOutput(priorTurns);
        if (block is null)
            return false;

        toolResults.Add(new ToolExecutionResult(
            ToolName: "FlightLookupByRoute",
            Input: "(prior_session_assistant)",
            Output: block,
            Success: true,
            DataSource: SyntheticDataSource,
            DurationMs: 0));
        logger?.LogInformation(
            "Injected synthetic FlightLookupByRoute from prior assistant turn (trust: IMemoryService role=assistant only; dataSource={Source}).",
            SyntheticDataSource);
        return true;
    }

    /// <summary>
    /// Builds tool output compatible with <see cref="VerifiedContextBuilder"/> / <see cref="ParseSingleFlight"/> patterns.
    /// </summary>
    private static string? TryBuildSyntheticRouteOutput(IReadOnlyList<ChatMessage> priorTurns)
    {
        foreach (var msg in priorTurns
                     .Where(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                     .Reverse())
        {
            var text = msg.Content;
            if (text.Length < 40)
                continue;

            if (!ContainsAlternativeContext(text))
                continue;

            var m = InlineRouteTimesRegex().Match(text);
            if (!m.Success)
                continue;

            var dep = m.Groups["dep"].Value;
            var arr = m.Groups["arr"].Value;
            var sd = m.Groups["sd"].Value.Trim();
            var sa = m.Groups["sa"].Value.Trim();
            if (dep.Length != 3 || arr.Length != 3)
                continue;

            var (flightLine, _) = ExtractFlightAndAirline(text);
            // "Found … alternative flight" satisfies self-correction regex; Flight line must come before any other "(" to avoid ParseSingleFlight grabbing the wrong parentheses.
            return
                "Found 1 alternative flight recovered from conversation history in this session.\n" +
                $"{flightLine}\n" +
                $"Route: {dep} → {arr}\n" +
                $"Scheduled departure: {sd}\n" +
                $"Scheduled arrival: {sa}\n";
        }

        return null;
    }

    private static bool ContainsAlternativeContext(string text) =>
        text.Contains("alternative", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ALTERNATIVE", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("rebooking", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("replacement flight", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("other flight", StringComparison.OrdinalIgnoreCase);

    private static (string flightLine, string airline) ExtractFlightAndAirline(string text)
    {
        var fm = FlightUnknownAirlineRegex().Match(text);
        if (fm.Success)
        {
            var token = fm.Groups["fn"].Value.Trim();
            var air = fm.Groups["air"].Value.Trim();
            if (token.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return ($"Flight Unknown ({air}): status from prior turn", air);
            return ($"Flight {token.Replace(" ", "")} ({air}): status from prior turn", air);
        }

        return ("Flight Unknown (from prior conversation): status unknown", "Prior conversation");
    }

    /// <summary>
    /// Comma-separated or flowing text as emitted in verified-facts / assistant prose.
    /// </summary>
    [GeneratedRegex(
        @"route\s+(?<dep>[A-Z]{3})\s*→\s*(?<arr>[A-Z]{3})[,\s]+scheduled\s+departure\s+(?<sd>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})[,\s]+scheduled\s+arrival\s+(?<sa>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineRouteTimesRegex();

    [GeneratedRegex(
        @"Flight\s+(?<fn>Unknown|[A-Z]{2}\s?\d{1,4})\s*\((?<air>[^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlightUnknownAirlineRegex();
}
