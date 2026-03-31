namespace TravelDisruptionAgent.Application.Options;

/// <summary>Agentic RAG loop: model-driven tool and retrieval invocations.</summary>
public class AgenticOptions
{
    public const string SectionName = "Agentic";

    /// <summary>When true and LLM is configured, use the agent loop instead of routed one-shot RAG.</summary>
    public bool EnableAgenticRag { get; set; } = true;

    public int MaxIterations { get; set; } = 4;

    public int MaxConsecutiveParseFailures { get; set; } = 2;

    /// <summary>Stop if the same normalized policy query is issued this many times.</summary>
    public int MaxIdenticalPolicyQueries { get; set; } = 2;

    /// <summary>Stop after this many iterations with no new tool/retrieval observation text.</summary>
    public int MaxStagnationIterations { get; set; } = 3;

    public int DefaultPolicyTopK { get; set; } = 1;

    /// <summary>Keep this many full observation blocks; older ones roll into one-line summaries.</summary>
    public int TranscriptRetentionObservations { get; set; } = 2;

    /// <summary>Soft cap on transcript characters sent back to the model (tail preserved).</summary>
    public int TranscriptMaxChars { get; set; } = 6000;

    /// <summary>When NeedsRag but KB returned no usable passages after at least one search.</summary>
    public string PolicyInsufficientKbMessage { get; set; } =
        "No sufficient company policy was found in the knowledge base for this question; verified policy wording cannot be provided.";

    /// <summary>When NeedsRag but the loop never retrieved policy before finalizing.</summary>
    public string PolicyNoRetrievalMessage { get; set; } =
        "Policy knowledge was not retrieved in this run; policy claims cannot be verified.";
}
