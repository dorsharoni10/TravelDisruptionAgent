namespace TravelDisruptionAgent.Application.Options;

public class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Minimum cosine similarity vs policy anchors to treat the message as policy-related (scope + routing).</summary>
    public double PolicyRoutingMinSimilarity { get; set; } = 0.38;

    /// <summary>Minimum similarity to include a document in semantic retrieval results.</summary>
    public double DocumentRetrievalMinSimilarity { get; set; } = 0.32;

    public int DefaultTopK { get; set; } = 3;

    public int ToolsOnlyTopK { get; set; } = 2;

    /// <summary>When &gt; 0, truncate each retrieved policy chunk to this many characters (saves tokens in agent + recommendation).</summary>
    public int MaxChunkCharsForRetrieval { get; set; } = 0;
}
