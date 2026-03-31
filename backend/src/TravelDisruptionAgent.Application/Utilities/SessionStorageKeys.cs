namespace TravelDisruptionAgent.Application.Utilities;

/// <summary>Isolates chat sessions per authenticated user in the session store (Mongo or in-memory).</summary>
public static class SessionStorageKeys
{
    internal const char Separator = '\u001e';

    public static string Compose(string ownerUserId, string clientSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSessionId);
        if (ownerUserId.Contains(Separator) || clientSessionId.Contains(Separator))
            throw new ArgumentException("User id or session id contains an illegal character.");
        return string.Concat(ownerUserId, Separator, clientSessionId);
    }
}
