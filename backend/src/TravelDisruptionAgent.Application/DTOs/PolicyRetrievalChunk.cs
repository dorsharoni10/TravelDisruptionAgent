namespace TravelDisruptionAgent.Application.DTOs;

public sealed record PolicyRetrievalChunk(string DocumentId, string Content, double? SimilarityScore);

public sealed record PolicyRetrievalDetail(
    IReadOnlyList<PolicyRetrievalChunk> Chunks,
    double? BestSimilarity,
    int CandidatesAboveThreshold);
