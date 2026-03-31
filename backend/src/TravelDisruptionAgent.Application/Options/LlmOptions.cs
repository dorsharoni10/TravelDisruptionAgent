namespace TravelDisruptionAgent.Application.Options;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = "gemini-2.5-flash-lite";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";
    public bool EnableStreaming { get; set; } = true;
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 4096;

    // Legacy compat
    public string ModelId { get => Model; set => Model = value; }
    public string DeploymentName { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
