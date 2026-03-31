namespace TravelDisruptionAgent.Application.Options;

/// <summary>Persistent multi-turn chat. If <see cref="ConnectionString"/> is empty, <see cref="InMemoryConversationSessionStore"/> is used.</summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    /// <summary>e.g. mongodb://localhost:27017 — leave empty for in-memory sessions only.</summary>
    public string ConnectionString { get; set; } = "";

    public string DatabaseName { get; set; } = "TravelDisruptionAgent";

    public string SessionsCollection { get; set; } = "conversation_sessions";
}
