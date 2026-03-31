using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TravelDisruptionAgent.Application.Telemetry;

/// <summary>OpenTelemetry traces + runtime metrics for the agentic loop (export via OTLP in the API host).</summary>
public static class AgenticTelemetry
{
    public const string ActivitySourceName = "TravelDisruptionAgent.Agentic";
    public const string MeterName = "TravelDisruptionAgent.Agentic";

    public static readonly ActivitySource Activity = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> LoopsCompleted = Meter.CreateCounter<long>(
        "agentic.loops.completed",
        description: "Agentic loop runs finished (any outcome)");

    public static readonly Histogram<int> LoopIterations = Meter.CreateHistogram<int>(
        "agentic.loops.iterations",
        description: "Iteration count per loop run");

    public static readonly Histogram<int> PolicyRetrievals = Meter.CreateHistogram<int>(
        "agentic.policy.retrievals",
        description: "Policy retrieval calls per loop");

    public static readonly Counter<long> LoopsSucceeded = Meter.CreateCounter<long>(
        "agentic.loops.succeeded",
        description: "Loops that returned a final answer successfully");

    public static readonly Counter<long> LoopsFailed = Meter.CreateCounter<long>(
        "agentic.loops.failed",
        description: "Loops that ended without success");

    public static readonly Counter<long> StopReason = Meter.CreateCounter<long>(
        "agentic.loops.stop_reason",
        description: "Count per stop reason (dimension: reason)");

    public static readonly Histogram<double> RetrievalBestSimilarity = Meter.CreateHistogram<double>(
        "agentic.retrieval.best_similarity",
        description: "Best similarity score from policy retrieval invocations");
}
