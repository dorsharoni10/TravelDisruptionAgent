using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TravelDisruptionAgent.Application.Constants;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class ConversationRouterTests
{
    private static Mock<IPlanningService> CreatePlanningMock()
    {
        var p = new Mock<IPlanningService>();
        p.Setup(x => x.CreateDeterministicPlan(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()))
            .Returns((string _, bool __, string? ___) => new AgentPlan("Test goal", new List<string> { "Step 1", "Step 2" }));
        return p;
    }

    private static ConversationRouter CreateRouter(Mock<IRagService>? ragMock = null)
    {
        var kernelFactory = new Mock<IKernelFactory>();
        kernelFactory.Setup(k => k.IsConfigured).Returns(false);
        var rag = ragMock?.Object ?? CreateDefaultRagMock().Object;
        return new ConversationRouter(
            kernelFactory.Object, rag, CreatePlanningMock().Object, NullLogger<ConversationRouter>.Instance);
    }

    private static Mock<IRagService> CreateDefaultRagMock()
    {
        var m = new Mock<IRagService>();
        m.Setup(r => r.GetPolicyRagHintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyRagHints(false, 0, []));
        return m;
    }

    [Theory]
    [InlineData("My flight was cancelled, what should I do?")]
    [InlineData("There's a storm and my airport is closed")]
    [InlineData("I need to rebook my connection")]
    public async Task RouteAsync_DisruptionLanguage_InScopeWithToolsAndRag(string message)
    {
        var r = await CreateRouter().RouteAsync(message);
        r.InScope.Should().BeTrue();
        r.NeedsTools.Should().BeTrue();
        r.NeedsRag.Should().BeTrue();
    }

    [Fact]
    public async Task RouteAsync_PolicyQuestion_SemanticInScope()
    {
        var rag = CreateDefaultRagMock();
        rag.Setup(x => x.GetPolicyRagHintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyRagHints(true, 0.55, ["compensation-rights"]));

        var r = await CreateRouter(rag).RouteAsync("What does my employer cover if my trip is disrupted?");
        r.InScope.Should().BeTrue();
        r.NeedsRag.Should().BeTrue();
        r.Intent.Should().Be(AgentIntents.ReimbursementExpenses);
    }

    [Fact]
    public async Task RouteAsync_EligibilityMeals_SemanticInScope()
    {
        var rag = CreateDefaultRagMock();
        rag.Setup(x => x.GetPolicyRagHintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyRagHints(true, 0.5, ["expense-limits"]));

        var r = await CreateRouter(rag).RouteAsync("Am I eligible for meals?");
        r.InScope.Should().BeTrue();
        r.NeedsRag.Should().BeTrue();
    }

    [Fact]
    public async Task RouteAsync_FlightNumber_InScopeOperational()
    {
        var r = await CreateRouter().RouteAsync("What is the status of flight UA234?");
        r.InScope.Should().BeTrue();
        r.NeedsTools.Should().BeTrue();
        r.Intent.Should().Be(AgentIntents.FlightOperational);
    }

    [Fact]
    public async Task RouteAsync_Joke_OutOfScope()
    {
        var r = await CreateRouter().RouteAsync("Tell me a joke");
        r.InScope.Should().BeFalse();
    }

    [Fact]
    public async Task RouteAsync_MealPolicy_SemanticInScope()
    {
        var rag = CreateDefaultRagMock();
        rag.Setup(x => x.GetPolicyRagHintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyRagHints(true, 0.48, ["expense-limits"]));

        var r = await CreateRouter(rag).RouteAsync("What is the meal policy?");
        r.InScope.Should().BeTrue();
        r.NeedsRag.Should().BeTrue();
    }

    [Fact]
    public void IntentCatalog_CancellationMessage_MapsToFlightCancellationWorkflow()
    {
        IntentCatalog.ToWorkflowIntent(AgentIntents.DisruptionAssistance,
                "My flight UA234 was cancelled")
            .Should().Be("flight_cancellation");
    }

    [Fact]
    public void IntentCatalog_PolicyIntent_MapsToGeneralTravelWorkflow()
    {
        IntentCatalog.ToWorkflowIntent(AgentIntents.CompanyTravelPolicy, "expense limits")
            .Should().Be("general_travel_disruption");
    }
}
