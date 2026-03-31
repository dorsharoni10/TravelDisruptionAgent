namespace TravelDisruptionAgent.Application.Telemetry;

/// <summary>
/// Strips query strings and fragments from URIs before trace/logging tags so API keys and PII in query parameters are not exported.
/// </summary>
public static class HttpUriRedaction
{
    public static Uri? WithoutQueryAndFragment(Uri? uri)
    {
        if (uri is null)
            return null;

        var b = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        return b.Uri;
    }
}
