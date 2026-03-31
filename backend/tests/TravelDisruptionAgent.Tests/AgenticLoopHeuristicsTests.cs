using FluentAssertions;
using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class AgenticLoopHeuristicsTests
{
    [Theory]
    [InlineData("  Meal  Allowance  ", "meal allowance")]
    [InlineData("Same QUERY", "same query")]
    public void NormalizePolicyQuery_collapses_whitespace_and_lowercases(string input, string expected) =>
        AgenticLoopHeuristics.NormalizePolicyQuery(input).Should().Be(expected);

    [Fact]
    public void ObservationsContentEqual_ignores_extra_whitespace() =>
        AgenticLoopHeuristics.ObservationsContentEqual("a  b", "a b").Should().BeTrue();
}
