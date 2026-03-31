using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class AgenticToolExecutorTests
{
    [Fact]
    public async Task Search_policy_knowledge_records_chunks_and_telemetry()
    {
        var rag = new Mock<IRagService>();
        rag.Setup(r => r.RetrievePolicyKnowledgeDetailedAsync("meals", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyRetrievalDetail(
                [new PolicyRetrievalChunk("policy-expenses", "Meal allowance $50", 0.91)],
                0.91,
                1));

        var tools = new Mock<IToolExecutionCoordinator>();
        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        var result = await executor.InvokeAsync(
            "search_policy_knowledge",
            new Dictionary<string, string?> { ["query"] = "meals" },
            new AgenticToolContext("user", DateTime.UtcNow));

        result.WasPolicyRetrieval.Should().BeTrue();
        result.PolicyChunkCount.Should().Be(1);
        result.BestSimilarity.Should().Be(0.91);
        result.PolicyChunkTexts.Should().ContainSingle().Which.Should().Contain("Meal");
        result.ToolRecords.Should().ContainSingle(t => t.ToolName == "search_policy_knowledge" && t.Success);
    }

    [Fact]
    public async Task Unknown_capability_returns_failed_tool_row()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        var result = await executor.InvokeAsync(
            "not_a_real_tool",
            new Dictionary<string, string?>(),
            new AgenticToolContext("hi", DateTime.UtcNow));

        result.ToolRecords.Should().ContainSingle(t => !t.Success);
        result.Observation.Should().Contain("Unknown capability");
    }

    [Fact]
    public async Task Lookup_flight_by_number_rejects_model_only_flight_not_in_user_text()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        var result = await executor.InvokeAsync(
            "lookup_flight_by_number",
            new Dictionary<string, string?> { ["flight_number"] = "TL194" },
            new AgenticToolContext("Flying TLV to ATH tomorrow, weather looks bad.", DateTime.UtcNow));

        tools.Verify(
            t => t.LookupFlightByNumberAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.ToolRecords.Should().ContainSingle(t =>
            !t.Success && t.ErrorMessage == "flight_number_not_in_user_message");
        result.Observation.Should().Contain("ignored");
    }

    [Fact]
    public async Task Lookup_flight_by_number_uses_user_message_when_model_passes_different_number()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        tools.Setup(t => t.LookupFlightByNumberAsync("LY315", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult("FlightLookup", "in", "ok", true, "Flight", 1, null));

        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        await executor.InvokeAsync(
            "lookup_flight_by_number",
            new Dictionary<string, string?> { ["flight_number"] = "TL194" },
            new AgenticToolContext("My flight is LY315 from TLV", DateTime.UtcNow));

        tools.Verify(t => t.LookupFlightByNumberAsync("LY315", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Search_flight_context_does_not_lookup_by_hallucinated_number()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        tools.Setup(t => t.LookupFlightByRouteAsync("TLV", "ATH", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult("Route", "in", "route ok", true, "Flight", 1, null));
        tools.Setup(t => t.LookupWeatherAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult("Weather", "", "wx ok", true, "Weather", 1, null));

        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        await executor.InvokeAsync(
            "search_flight_context",
            new Dictionary<string, string?>
            {
                ["flight_number"] = "TL194",
                ["origin"] = "TLV",
                ["destination"] = "ATH"
            },
            new AgenticToolContext("Find available flights from TLV to ATH today", DateTime.UtcNow));

        tools.Verify(
            t => t.LookupFlightByNumberAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tools.Verify(
            t => t.LookupFlightByRouteAsync("TLV", "ATH", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
