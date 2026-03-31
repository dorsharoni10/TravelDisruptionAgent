using Microsoft.Extensions.Logging.Abstractions;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class OrchestrationTraceLoggerTests
{
    [Fact]
    public void LogExecutionTrace_redact_mode_does_not_throw()
    {
        var tools = new List<ToolExecutionResult>
        {
            new("FlightLookupByNumber", "SECRET_INPUT", "output", true, "mock", 0)
        };
        var ctx = VerifiedContextBuilder.Build(
            "flight_cancellation",
            tools,
            null,
            new PlanValidationResult([], true, [], []),
            requestTime: DateTime.UtcNow);

        OrchestrationTraceLogger.LogExecutionTrace(
            NullLogger.Instance,
            new List<string> { "step 1" },
            tools,
            ctx,
            redactSensitive: true);
    }

    [Fact]
    public void LogRouteAlternativePipeline_redact_mode_does_not_throw()
    {
        var diag = new RouteAlternativePipelineDiagnostics(
            NormalizedInputRows: 3,
            ExcludedAsUserPrimaryFlightNumber: 1,
            PoolAfterQualityFilters: 2,
            Tiers: Array.Empty<RouteAlternativeTierDiagnostics>(),
            FinalAlternativeCount: 1,
            SampleDepartureTimesUtc: "2026-01-01");

        OrchestrationTraceLogger.LogRouteAlternativePipeline(
            NullLogger.Instance,
            diag,
            primaryFlightNumbers: new HashSet<string> { "LY001" },
            redactSensitive: true);
    }
}
