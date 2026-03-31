using TravelDisruptionAgent.Application.Telemetry;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class HttpUriRedactionTests
{
    [Fact]
    public void WithoutQueryAndFragment_returns_null_for_null()
    {
        Assert.Null(HttpUriRedaction.WithoutQueryAndFragment(null));
    }

    [Fact]
    public void WithoutQueryAndFragment_strips_query_and_fragment()
    {
        var uri = new Uri("https://api.example.com/v1/endpoint?access_key=SECRET&foo=1#frag");
        var redacted = HttpUriRedaction.WithoutQueryAndFragment(uri);

        Assert.NotNull(redacted);
        Assert.Equal("https://api.example.com/v1/endpoint", redacted!.GetLeftPart(UriPartial.Path));
        Assert.DoesNotContain("SECRET", redacted.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain('?', redacted.ToString());
    }
}
