namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>Maps provider exceptions to client-safe messages (no raw exception text).</summary>
public static class LlmErrorDescriptions
{
    private const string GenericFailure =
        "The AI service returned an unexpected error. Please try again in a moment.";

    public static string GetPublicMessage(Exception ex)
    {
        var blob = CollectExceptionMessages(ex);

        if (blob.Contains("403", StringComparison.Ordinal))
            return "Gemini returned 403 Forbidden — verify API key permissions and that the Generative Language API is enabled.";
        if (blob.Contains("401", StringComparison.Ordinal))
            return "Gemini returned 401 Unauthorized — the API key may be invalid or revoked.";
        if (blob.Contains("429", StringComparison.Ordinal) ||
            blob.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
            return "Gemini returned HTTP 429 (Too Many Requests). That is a rate limit or usage quota on the generative API — wait briefly and retry, or check quota in Google AI Studio / Cloud Console.";
        if (blob.Contains("404", StringComparison.Ordinal))
            return "Model or endpoint not found (404) — check the model name in configuration.";
        if (blob.Contains("Resource exhausted", StringComparison.OrdinalIgnoreCase) ||
            blob.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return "The provider reported quota or resource limits exhausted — check billing and quotas.";

        return GenericFailure;
    }

    private static string CollectExceptionMessages(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
            parts.Add(e.Message);
        return string.Join(' ', parts);
    }
}
