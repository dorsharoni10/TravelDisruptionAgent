using System.Security.Claims;

namespace TravelDisruptionAgent.Api.Security;

public static class SessionStorageKeyBuilder
{
    /// <summary>Unit separator between namespace and client session id (matches orchestrator).</summary>
    public const char NamespaceSeparator = '\u001f';

    /// <summary>Builds the same persistence key the orchestrator uses for chat persistence.</summary>
    /// <remarks>
    /// When <paramref name="authEnabled"/> is true and <paramref name="user"/> lacks <see cref="ClaimTypes.NameIdentifier"/>,
    /// the result is the raw client session id (same as anonymous). Callers that require user isolation must reject that case
    /// (e.g. return 401) before using the key for destructive operations.
    /// </remarks>
    public static string Build(bool authEnabled, ClaimsPrincipal? user, string clientSessionId)
    {
        var sid = clientSessionId.Trim();
        if (!authEnabled)
            return sid;

        var sub = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return sid;

        return $"{sub}{NamespaceSeparator}{sid}";
    }
}
