using Microsoft.Extensions.Logging;
using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Services;

/// <summary>Structured execution trace logging for <see cref="AgentOrchestrator"/>.</summary>
public static class OrchestrationTraceLogger
{
    private const string Redacted = "[redacted]";

    public static void LogExecutionTrace(
        ILogger logger,
        IReadOnlyList<string> planSteps,
        IReadOnlyList<ToolExecutionResult> toolResults,
        VerifiedContext ctx,
        bool redactSensitive = false)
    {
        logger.LogInformation("──── Execution Trace ────");

        logger.LogInformation("[PLANNED] {Count} steps:", planSteps.Count);
        foreach (var step in planSteps)
            logger.LogInformation("  • {Step}", redactSensitive ? Redacted : step);

        var executed = toolResults.Where(t => t.Success).ToList();
        var failed = toolResults.Where(t => !t.Success).ToList();

        logger.LogInformation("[EXECUTED] {Count} tool(s) succeeded:", executed.Count);
        foreach (var t in executed)
        {
            var input = redactSensitive ? Redacted : t.Input;
            logger.LogInformation("  ✓ {Tool}({Input}) [{Source}]", t.ToolName, input, t.DataSource);
        }

        if (failed.Count > 0)
        {
            logger.LogWarning("[FAILED] {Count} tool(s) failed:", failed.Count);
            foreach (var t in failed)
            {
                var input = redactSensitive ? Redacted : t.Input;
                var err = redactSensitive ? Redacted : t.ErrorMessage;
                logger.LogWarning("  ✗ {Tool}({Input}): {Error}", t.ToolName, input, err);
            }
        }

        if (ctx.SkippedSteps.Count > 0)
        {
            logger.LogWarning("[SKIPPED] {Count} planned step(s) not executed:", ctx.SkippedSteps.Count);
            foreach (var s in ctx.SkippedSteps)
            {
                if (redactSensitive)
                    logger.LogWarning("  ⊘ {Step}", Redacted);
                else
                    logger.LogWarning("  ⊘ {Step}: {Reason}", s.StepDescription, s.Reason);
            }
        }

        logger.LogInformation("[VERIFIED FACTS] {Count} fact(s) used in answer:", ctx.VerifiedFacts.Count);
        if (redactSensitive)
            logger.LogInformation("  (fact text redacted)");
        else
        {
            foreach (var fact in ctx.VerifiedFacts)
                logger.LogInformation("  ▸ {Fact}", fact);
        }

        if (ctx.UnverifiedFacts.Count > 0)
        {
            logger.LogWarning("[UNVERIFIED] {Count} fact(s) excluded or flagged:", ctx.UnverifiedFacts.Count);
            if (redactSensitive)
                logger.LogWarning("  (fact text redacted)");
            else
            {
                foreach (var fact in ctx.UnverifiedFacts)
                    logger.LogWarning("  ▹ {Fact}", fact);
            }
        }

        if (ctx.Contradictions.Count > 0)
        {
            logger.LogWarning("[CONTRADICTIONS] {Count}:", ctx.Contradictions.Count);
            if (redactSensitive)
                logger.LogWarning("  (details redacted)");
            else
            {
                foreach (var c in ctx.Contradictions)
                    logger.LogWarning("  ⚠ {Contradiction}", c);
            }
        }

        logger.LogInformation("──── End Trace ────");
    }

    public static void LogRouteAlternativePipeline(
        ILogger logger,
        RouteAlternativePipelineDiagnostics diag,
        HashSet<string>? primaryFlightNumbers = null,
        bool redactSensitive = false)
    {
        if (redactSensitive)
        {
            logger.LogInformation(
                "Route alternative filter: inputRows={Input}, final={Final} (details redacted)",
                diag.NormalizedInputRows,
                diag.FinalAlternativeCount);
            return;
        }

        var tierSummary = string.Join(
            "; ",
            diag.Tiers.Select(t =>
                $"{t.HorizonHours}h: inWindow={t.CountInTimeWindow}, afterDedup={t.CountAfterCodeshareDedup}"));
        var primaryCsv = primaryFlightNumbers is { Count: > 0 }
            ? string.Join(", ", primaryFlightNumbers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            : "(none)";
        logger.LogInformation(
            "Route alternative filter: primaryFlightNumbers=[{Primary}], inputRows={Input}, excludedPrimaryNumberMatches={Excl}, poolAfterQuality={Pool}, final={Final}, tiers=[{Tiers}], sampleDepUtc=[{Samples}]",
            primaryCsv,
            diag.NormalizedInputRows,
            diag.ExcludedAsUserPrimaryFlightNumber,
            diag.PoolAfterQualityFilters,
            diag.FinalAlternativeCount,
            tierSummary,
            diag.SampleDepartureTimesUtc ?? "");
    }

    public static string ResolveRouteSearchStepTitle(string rawRouteOutput, RouteAlternativePipelineDiagnostics diag)
    {
        if (diag.FinalAlternativeCount > 0)
            return "Route Search: Flights Found";
        var declared = VerifiedContextBuilder.TryParseProviderFlightCountFromOutput(rawRouteOutput);
        if (diag.NormalizedInputRows > 0 || declared > 0)
            return "Route Search: Data received (none in rebooking window)";
        return "Route Search: No matching alternatives";
    }
}
