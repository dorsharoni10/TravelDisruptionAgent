using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Google;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Infrastructure.SemanticKernel;

public class SemanticKernelFactory : IKernelFactory
{
    private readonly LlmOptions _options;
    private readonly ILogger<SemanticKernelFactory> _logger;

    public SemanticKernelFactory(
        IOptions<LlmOptions> options,
        ILogger<SemanticKernelFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public Kernel CreateKernel()
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("LLM API key not configured — kernel will not have chat completion");
            return Kernel.CreateBuilder().Build();
        }

        _logger.LogDebug("Creating Semantic Kernel: provider={Provider}, model={Model}",
            _options.Provider, _options.Model);

        var builder = Kernel.CreateBuilder();

        switch (_options.Provider.ToLowerInvariant())
        {
            case "gemini":
            case "google":
            case "googleai":
                builder.AddGoogleAIGeminiChatCompletion(
                    modelId: _options.Model,
                    apiKey: _options.ApiKey);
                break;

            case "azureopenai":
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: _options.DeploymentName,
                    endpoint: _options.Endpoint,
                    apiKey: _options.ApiKey);
                break;

            case "openai":
            default:
                builder.AddOpenAIChatCompletion(
                    modelId: _options.Model,
                    apiKey: _options.ApiKey);
                break;
        }

        return builder.Build();
    }
}
