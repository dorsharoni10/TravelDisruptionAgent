using TravelDisruptionAgent.Application.DTOs;

namespace TravelDisruptionAgent.Application.Interfaces;

public interface IRagService
{
    /// <summary>Embedding similarity vs policy anchors and seed-doc summaries; drives scope and RAG routing.</summary>
    Task<PolicyRagHints> GetPolicyRagHintsAsync(
        string userMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user message is semantically close to company policy / compensation / expenses
    /// (embedding match against fixed English anchors). Returns false if the LLM API is not configured
    /// or embedding fails.
    /// </summary>
    Task<bool> MessageSuggestsPolicyKnowledgeAsync(
        string userMessage,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> RetrieveRelevantContextAsync(
        string query,
        int topK = 3,
        CancellationToken cancellationToken = default);

    /// <summary>Policy retrieval with document ids and similarity scores (for agentic loop telemetry).</summary>
    Task<PolicyRetrievalDetail> RetrievePolicyKnowledgeDetailedAsync(
        string query,
        int topK = 3,
        CancellationToken cancellationToken = default);

    Task IndexDocumentAsync(
        string documentId,
        string content,
        CancellationToken cancellationToken = default);

    Task SeedAsync(CancellationToken cancellationToken = default);
    int DocumentCount { get; }
}
