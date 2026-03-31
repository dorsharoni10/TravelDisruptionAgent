using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TravelDisruptionAgent.Application.Constants;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using TravelDisruptionAgent.Application.Utilities;
using TravelDisruptionAgent.Infrastructure.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

/// <summary>
/// End-to-end through <see cref="AgentOrchestrator"/>: session history from turn 1 (simulated via memory) + follow-up user message on turn 2.
/// </summary>
public class FollowUpTurnsOrchestratorE2ETests
{
    private const string PriorAssistantWithAlternative =
        "▸ [ALTERNATIVE — filtered route search] Flight Unknown (Israir Airlines): status is \"Unknown\", " +
        "route TLV → ATH, scheduled departure 2026-03-30 23:30, scheduled arrival 2026-03-31 01:37";

    /// <summary>
    /// Route backfill calls live <see cref="IToolExecutionCoordinator.LookupFlightByRouteAsync"/>; failing it
    /// exercises the <c>conversation_history_fallback</c> path (successful route row still missing).
    /// </summary>
    private static Mock<IToolExecutionCoordinator> CreateFailingRouteCoordinator()
    {
        var coord = new Mock<IToolExecutionCoordinator>();
        coord.Setup(c => c.LookupFlightByRouteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult(
                "FlightLookupByRoute",
                "TLV → ATH on 2026-03-30",
                "No flights (test double)",
                false,
                "test:route",
                0,
                ErrorMessage: "simulated failure"));
        coord.Setup(c => c.LookupFlightByNumberAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult("FlightLookupByNumber", "", "none", false, "test", 0));
        coord.Setup(c => c.LookupWeatherAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult("WeatherLookup", "", "none", false, "test", 0));
        return coord;
    }

    private static AgentOrchestrator CreateOrchestrator(
        IConversationRouter router,
        OrchestrationOptions? orchestrationOptions = null,
        IMemoryService? memoryOverride = null,
        IToolExecutionCoordinator? toolCoordinator = null)
    {
        var kernelFactory = new Mock<IKernelFactory>();
        kernelFactory.Setup(k => k.IsConfigured).Returns(false);

        var rag = new RagService(
            new NoOpHttpClientFactory(),
            Options.Create(new LlmOptions()),
            Options.Create(new RagOptions()),
            NullLogger<RagService>.Instance);
        rag.SeedAsync().GetAwaiter().GetResult();

        var planningService = new PlanningService(
            kernelFactory.Object, NullLogger<PlanningService>.Instance);

        var coordinator = toolCoordinator ?? CreateFailingRouteCoordinator().Object;
        var toolPipeline = new AgentToolExecutionPipeline(
            coordinator,
            Options.Create(orchestrationOptions ?? new OrchestrationOptions()),
            NullLogger<AgentToolExecutionPipeline>.Instance);

        var recommendationService = new RecommendationService(
            kernelFactory.Object, NullLogger<RecommendationService>.Instance);
        var selfCorrection = new SelfCorrectionService(
            coordinator, NullLogger<SelfCorrectionService>.Instance);
        var guardrails = new GuardrailsService(NullLogger<GuardrailsService>.Instance);

        var memory = memoryOverride ?? new MemoryService(
            new InMemoryConversationSessionStore(Options.Create(new ConversationSessionOptions())),
            NullLogger<MemoryService>.Instance);

        var agentLoop = new Mock<IAgentLoopService>();

        return new AgentOrchestrator(
            router,
            toolPipeline,
            rag,
            recommendationService,
            selfCorrection,
            guardrails,
            memory,
            kernelFactory.Object,
            agentLoop.Object,
            Options.Create(new AgenticOptions { EnableAgenticRag = false }),
            Options.Create(new ConversationSessionOptions()),
            Options.Create(orchestrationOptions ?? new OrchestrationOptions()),
            NullLogger<AgentOrchestrator>.Instance);
    }

    private static Mock<IConversationRouter> CreateInScopeRouterWithoutTools()
    {
        var route = new ConversationRouteResult(
            InScope: true,
            RejectionMessage: null,
            Intent: AgentIntents.DisruptionAssistance,
            NeedsTools: false,
            NeedsRag: false,
            UseInternalOnly: false,
            Reason: "test",
            SuggestedRagTopics: [],
            LlmFallbackReason: null);
        var plan = new AgentPlan("Assist with disruption", new[] { "Use session context to answer follow-up questions." });

        var router = new Mock<IConversationRouter>();
        router
            .Setup(r => r.RouteAndPlanAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RouteAndPlanResult(route, plan, null));
        return router;
    }

    [Fact]
    public async Task Second_turn_injects_conversation_history_route_fallback_when_enabled()
    {
        var router = CreateInScopeRouterWithoutTools();
        var sessionId = "e2e-followup-session";
        var memory = new MemoryService(
            new InMemoryConversationSessionStore(Options.Create(new ConversationSessionOptions())),
            NullLogger<MemoryService>.Instance);
        var orchestrator = CreateOrchestrator(
            router.Object,
            new OrchestrationOptions { EnableConversationHistoryRouteFallback = true },
            memory,
            CreateFailingRouteCoordinator().Object);

        var now = DateTime.UtcNow;
        await memory.AppendMessageAsync(sessionId, new ChatMessage("user", "First question about my trip", now), CancellationToken.None);
        await memory.AppendMessageAsync(sessionId, new ChatMessage("assistant", PriorAssistantWithAlternative, now), CancellationToken.None);

        var response = await orchestrator.ProcessAsync(new ChatRequest(
            "What was the scheduled arrival time for the alternative flight you found?",
            SessionId: sessionId,
            UserId: "e2e-user"));

        response.InScope.Should().BeTrue();
        response.ToolExecutions.Should().Contain(t =>
            t.ToolName == "FlightLookupByRoute" &&
            t.Success &&
            t.DataSource.Contains(ConversationHistoryRouteFallback.SyntheticDataSource, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Second_turn_does_not_inject_history_fallback_when_feature_disabled()
    {
        var router = CreateInScopeRouterWithoutTools();
        var sessionId = "e2e-followup-disabled";
        var memory = new MemoryService(
            new InMemoryConversationSessionStore(Options.Create(new ConversationSessionOptions())),
            NullLogger<MemoryService>.Instance);
        var orchestrator = CreateOrchestrator(
            router.Object,
            new OrchestrationOptions { EnableConversationHistoryRouteFallback = false },
            memory,
            CreateFailingRouteCoordinator().Object);

        var now = DateTime.UtcNow;
        await memory.AppendMessageAsync(sessionId, new ChatMessage("user", "First question", now), CancellationToken.None);
        await memory.AppendMessageAsync(sessionId, new ChatMessage("assistant", PriorAssistantWithAlternative, now), CancellationToken.None);

        var response = await orchestrator.ProcessAsync(new ChatRequest(
            "What was the scheduled arrival time for the alternative flight you found?",
            SessionId: sessionId,
            UserId: "e2e-user"));

        response.InScope.Should().BeTrue();
        response.ToolExecutions.Should().NotContain(t =>
            t.DataSource.Contains(ConversationHistoryRouteFallback.SyntheticDataSource, StringComparison.OrdinalIgnoreCase));
    }
}
