using TravelDisruptionAgent.Application.Utilities;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class LlmErrorDescriptionsTests
{
    [Fact]
    public void GetPublicMessage_unknown_exception_is_generic()
    {
        var msg = LlmErrorDescriptions.GetPublicMessage(new InvalidOperationException("internal stack trace stuff"));
        Assert.DoesNotContain("internal stack", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unexpected error", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPublicMessage_maps_429()
    {
        var msg = LlmErrorDescriptions.GetPublicMessage(new HttpRequestException("Response status code does not indicate success: 429."));
        Assert.Contains("429", msg, StringComparison.Ordinal);
    }
}
