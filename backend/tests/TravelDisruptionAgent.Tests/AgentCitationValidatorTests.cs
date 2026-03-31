using FluentAssertions;
using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class AgentCitationValidatorTests
{
    [Fact]
    public void Keeps_valid_policy_citation()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "policy-expenses" };
        var (text, w) = AgentCitationValidator.SanitizeCitations(
            "Per [policy-expenses] meals are covered.",
            allowed);
        text.Should().Contain("[policy-expenses]");
        w.Should().BeEmpty();
    }

    [Fact]
    public void Removes_unknown_policy_citation()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "policy-expenses" };
        var (text, w) = AgentCitationValidator.SanitizeCitations(
            "See [policy-fake] for rules.",
            allowed);
        text.Should().NotContain("policy-fake");
        w.Should().ContainSingle();
    }
}
