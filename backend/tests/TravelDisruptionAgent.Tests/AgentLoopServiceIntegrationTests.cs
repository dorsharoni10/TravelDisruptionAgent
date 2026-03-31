using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using TravelDisruptionAgent.Domain.Entities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class AgentLoopServiceIntegrationTests
{
    [Fact]
    public async Task Tool_then_final_succeeds_with_policy_evidence()
    {
        var rag = new Mock<IRagService>();
        rag.Setup(r => r.RetrievePolicyKnowledgeDetailedAsync("meals", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyRetrievalDetail(
                [new PolicyRetrievalChunk("policy-expenses", "Meal allowance text", 0.92)],
                0.92,
                1));

        var tools = new Mock<IToolExecutionCoordinator>();
        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions { MaxConsecutiveParseFailures = 2 }),
            NullLogger<AgenticToolExecutor>.Instance);

        var invoker = new QueueAgentLoopStepLlmInvoker(
        [
            AgentLlmStepTestData.StructuredTool("search_policy_knowledge", """{"query":"meals"}"""),
            AgentLlmStepTestData.StructuredFinal("According to [policy-expenses], meals apply.")
        ]);

        var loop = new AgentLoopService(
            invoker,
            executor,
            Options.Create(new AgenticOptions()),
            Options.Create(new RagOptions { DocumentRetrievalMinSimilarity = 0.32 }),
            NullLogger<AgentLoopService>.Instance);

        var route = new ConversationRouteResult(
            true, null, "company_travel_policy", false, true, false, "test", [], null);

        var result = await loop.RunAsync(
            "What is our meal allowance?",
            route,
            "general_travel_disruption",
            ["1. Check policy"],
            null,
            null);

        result.Succeeded.Should().BeTrue();
        result.FinalAnswer.Should().Contain("[policy-expenses]");
        result.PolicyRetrievalCount.Should().Be(1);
        result.CitationSanitizationWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Grounding_fallback_when_needs_rag_but_no_retrieval()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        var invoker = new QueueAgentLoopStepLlmInvoker([AgentLlmStepTestData.StructuredFinal("You get $500 per day.")]);

        var opts = new AgenticOptions
        {
            PolicyNoRetrievalMessage = "NO_RETRIEVAL_TEST",
            PolicyInsufficientKbMessage = "INSUFFICIENT_TEST"
        };

        var loop = new AgentLoopService(
            invoker,
            executor,
            Options.Create(opts),
            Options.Create(new RagOptions()),
            NullLogger<AgentLoopService>.Instance);

        var route = new ConversationRouteResult(
            true, null, "company_travel_policy", false, true, false, "test", [], null);

        var result = await loop.RunAsync("Policy question", route, "general_travel_disruption", [], null, null);

        result.Succeeded.Should().BeTrue();
        result.FinalAnswer.Should().Be("NO_RETRIEVAL_TEST");
        result.PolicyGroundingFallbackApplied.Should().BeTrue();
        result.StopReason.Should().Be(AgenticStopReason.GroundingFallback);
        rag.Verify(
            r => r.RetrievePolicyKnowledgeDetailedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Parse_failures_stop_after_max()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        var invoker = new QueueAgentLoopStepLlmInvoker(
        [
            AgentLlmStepTestData.Failed(),
            AgentLlmStepTestData.Failed()
        ]);

        var loop = new AgentLoopService(
            invoker,
            executor,
            Options.Create(new AgenticOptions { MaxConsecutiveParseFailures = 2 }),
            Options.Create(new RagOptions()),
            NullLogger<AgentLoopService>.Instance);

        var route = new ConversationRouteResult(
            true, null, "flight_operational", true, false, false, "test", [], null);

        var result = await loop.RunAsync("UA123", route, "flight_cancellation", [], null, null);

        result.Succeeded.Should().BeFalse();
        result.StopReason.Should().Be(AgenticStopReason.ParseFailures);
    }

    [Fact]
    public async Task Stagnation_stops_when_observation_repeats()
    {
        var rag = new Mock<IRagService>();
        var tools = new Mock<IToolExecutionCoordinator>();
        tools.Setup(t => t.LookupWeatherAsync("NYC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult(
                "WeatherLookup", "NYC", "SAME_OUTPUT", true, "mock", 1));

        var executor = new AgenticToolExecutor(
            rag.Object,
            tools.Object,
            Options.Create(new AgenticOptions()),
            NullLogger<AgenticToolExecutor>.Instance);

        var steps = Enumerable.Range(0, 5)
            .Select(_ => AgentLlmStepTestData.StructuredTool("lookup_weather", """{"location":"NYC"}"""))
            .ToArray();
        var invoker = new QueueAgentLoopStepLlmInvoker(steps);

        var loop = new AgentLoopService(
            invoker,
            executor,
            Options.Create(new AgenticOptions { MaxStagnationIterations = 3, MaxIterations = 20 }),
            Options.Create(new RagOptions()),
            NullLogger<AgentLoopService>.Instance);

        var route = new ConversationRouteResult(
            true, null, "weather_operational", true, false, false, "test", [], null);

        var result = await loop.RunAsync("Weather in NYC", route, "weather_disruption", [], null, null);

        result.Succeeded.Should().BeFalse();
        result.StopReason.Should().Be(AgenticStopReason.Stagnation);
    }
}
