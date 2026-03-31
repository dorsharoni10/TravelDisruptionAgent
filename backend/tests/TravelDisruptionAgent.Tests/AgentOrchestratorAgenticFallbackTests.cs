using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using TravelDisruptionAgent.Domain.Enums;
using TravelDisruptionAgent.Infrastructure.Providers;
using TravelDisruptionAgent.Infrastructure.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class AgentOrchestratorAgenticFallbackTests
{
    [Fact]
    public async Task Agent_loop_exception_falls_back_without_throwing()
    {
        var kernelFactory = new Mock<IKernelFactory>();
        kernelFactory.Setup(k => k.IsConfigured).Returns(true);

        var rag = new RagService(
            new NoOpHttpClientFactory(),
            Options.Create(new LlmOptions()),
            Options.Create(new RagOptions()),
            NullLogger<RagService>.Instance);
        await rag.SeedAsync();

        var planningService = new PlanningService(
            kernelFactory.Object, NullLogger<PlanningService>.Instance);
        var conversationRouter = new ConversationRouter(
            kernelFactory.Object, rag, planningService, NullLogger<ConversationRouter>.Instance);

        var mockWeather = new MockWeatherProvider(NullLogger<MockWeatherProvider>.Instance);
        var mockFlight = new MockFlightProvider(NullLogger<MockFlightProvider>.Instance);
        var toolCoordinator = new ToolExecutionCoordinator(
            mockWeather, mockFlight, mockWeather, mockFlight,
            NullLogger<ToolExecutionCoordinator>.Instance);

        var recommendationService = new RecommendationService(
            kernelFactory.Object, NullLogger<RecommendationService>.Instance);
        var selfCorrection = new SelfCorrectionService(
            toolCoordinator, NullLogger<SelfCorrectionService>.Instance);
        var guardrails = new GuardrailsService(NullLogger<GuardrailsService>.Instance);
        var sessionStore = new InMemoryConversationSessionStore(Options.Create(new ConversationSessionOptions()));
        var memory = new MemoryService(sessionStore, NullLogger<MemoryService>.Instance);

        var agentLoop = new Mock<IAgentLoopService>();
        agentLoop
            .Setup(a => a.RunAsync(
                It.IsAny<string>(),
                It.IsAny<ConversationRouteResult>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TravelDisruptionAgent.Domain.Entities.UserPreferences?>(),
                It.IsAny<Func<AgentStepType, string, string, Task>?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated agent failure"));

        var toolPipeline = new AgentToolExecutionPipeline(
            toolCoordinator,
            Options.Create(new OrchestrationOptions()),
            NullLogger<AgentToolExecutionPipeline>.Instance);
        var orchestrator = new AgentOrchestrator(
            conversationRouter,
            toolPipeline,
            rag,
            recommendationService,
            selfCorrection,
            guardrails,
            memory,
            kernelFactory.Object,
            agentLoop.Object,
            Options.Create(new AgenticOptions { EnableAgenticRag = true }),
            Options.Create(new ConversationSessionOptions()),
            Options.Create(new OrchestrationOptions()),
            NullLogger<AgentOrchestrator>.Instance);

        var response = await orchestrator.ProcessAsync(new ChatRequest(
            "My flight UA234 from NYC to London was cancelled. What should I do?",
            UserId: "t-user"));

        response.InScope.Should().BeTrue();
        response.AgenticLoopUsed.Should().BeFalse();
    }
}
