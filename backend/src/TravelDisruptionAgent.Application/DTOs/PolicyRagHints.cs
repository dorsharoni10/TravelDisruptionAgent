namespace TravelDisruptionAgent.Application.DTOs;

public sealed record PolicyRagHints(
    bool SuggestsPolicy,
    double BestSimilarity,
    IReadOnlyList<string> SuggestedRagTopics);
