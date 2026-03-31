using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Infrastructure.SemanticKernel;

/// <summary>
/// Structured agent step via Gemini <c>response_schema</c> or OpenAI <c>json_schema</c>, with one-shot legacy JSON fallback.
/// </summary>
public sealed class KernelAgentLoopStepLlmInvoker : IAgentLoopStepLlmInvoker
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private const string OpenAiAgentStepSchema = """
{
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "thought": { "type": "string" },
    "known_summary": { "type": "string" },
    "still_missing": { "type": "string" },
    "sufficient_evidence": { "type": "boolean" },
    "action": { "type": "string", "enum": ["invoke_tool", "final_answer"] },
    "capability": { "type": "string" },
    "arguments_json": { "type": "string" },
    "final_answer": { "type": "string" },
    "confidence": { "type": "number" }
  },
  "required": [
    "thought",
    "known_summary",
    "still_missing",
    "sufficient_evidence",
    "action",
    "capability",
    "arguments_json",
    "final_answer",
    "confidence"
  ]
}
""";

    private readonly IKernelFactory _kernelFactory;
    private readonly LlmOptions _llm;
    private readonly ILogger<KernelAgentLoopStepLlmInvoker> _logger;

    public KernelAgentLoopStepLlmInvoker(
        IKernelFactory kernelFactory,
        IOptions<LlmOptions> llmOptions,
        ILogger<KernelAgentLoopStepLlmInvoker> logger)
    {
        _kernelFactory = kernelFactory;
        _llm = llmOptions.Value;
        _logger = logger;
    }

    public bool IsConfigured => _kernelFactory.IsConfigured;

    public async Task<AgentLlmInvocationResult> InvokeStructuredStepAsync(
        string systemInstructions,
        string userTurnContent,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new AgentLlmInvocationResult
            {
                Succeeded = false,
                Error = "LLM not configured",
                Kind = AgentLlmInvocationKind.StructuredSchema
            };
        }

        var kernel = _kernelFactory.CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(systemInstructions);
        history.AddUserMessage(userTurnContent);

        var provider = _llm.Provider.Trim().ToLowerInvariant();
        PromptExecutionSettings settings = provider switch
        {
            "gemini" or "google" or "googleai" => BuildGeminiSettings(),
            "azureopenai" => BuildOpenAiSettings(),
            _ => BuildOpenAiSettings()
        };

        try
        {
            var msg = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken)
                .ConfigureAwait(false);
            var text = msg.Content?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return await LegacyFallbackAsync(kernel, systemInstructions, userTurnContent, cancellationToken)
                    .ConfigureAwait(false);

            var dto = TryDeserialize(text);
            if (dto is not null && !string.IsNullOrWhiteSpace(dto.Action))
            {
                return new AgentLlmInvocationResult
                {
                    Succeeded = true,
                    Output = dto,
                    Kind = AgentLlmInvocationKind.StructuredSchema
                };
            }

            _logger.LogWarning("Structured LLM returned empty action; trying legacy JSON path");
            return await LegacyFallbackAsync(kernel, systemInstructions, userTurnContent, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Structured agent LLM call failed; trying legacy JSON path");
            return await LegacyFallbackAsync(kernel, systemInstructions, userTurnContent, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private GeminiPromptExecutionSettings BuildGeminiSettings() =>
        new()
        {
            Temperature = _llm.Temperature,
            MaxTokens = _llm.MaxTokens,
            ResponseMimeType = "application/json",
            ResponseSchema = typeof(AgentLoopStructuredOutput)
        };

    private OpenAIPromptExecutionSettings BuildOpenAiSettings() =>
        new()
        {
            Temperature = (float)_llm.Temperature,
            MaxTokens = _llm.MaxTokens,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "travel_agent_step",
                jsonSchema: BinaryData.FromString(OpenAiAgentStepSchema),
                jsonSchemaIsStrict: true)
        };

    private async Task<AgentLlmInvocationResult> LegacyFallbackAsync(
        Kernel kernel,
        string systemInstructions,
        string userTurnContent,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = $"""
                {systemInstructions}

                ---
                {userTurnContent}

                Output a single JSON object only (no markdown). Required keys: thought, known_summary, still_missing, sufficient_evidence, action (invoke_tool or final_answer), capability, arguments_json (string containing a serialized JSON object; use empty object if none), final_answer, confidence.
                """;
            var fnResult = await kernel.InvokePromptAsync(prompt, new KernelArguments(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var raw = fnResult.GetValue<string>() ?? "";
            var json = ExtractJsonObject(raw);
            var dto = TryDeserialize(json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Action))
            {
                return new AgentLlmInvocationResult
                {
                    Succeeded = false,
                    Error = "legacy_json_parse_failed",
                    RawSnippet = Truncate(json, 500),
                    Kind = AgentLlmInvocationKind.LegacyTextJson
                };
            }

            return new AgentLlmInvocationResult
            {
                Succeeded = true,
                Output = dto,
                Kind = AgentLlmInvocationKind.LegacyTextJson
            };
        }
        catch (Exception ex)
        {
            return new AgentLlmInvocationResult
            {
                Succeeded = false,
                Error = ex.Message,
                Kind = AgentLlmInvocationKind.LegacyTextJson
            };
        }
    }

    private static AgentLoopStructuredOutput? TryDeserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentLoopStructuredOutput>(text, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJsonObject(string raw)
    {
        var t = raw.Trim();
        const string fence = "```";
        if (t.Contains(fence, StringComparison.Ordinal))
        {
            var first = t.IndexOf(fence, StringComparison.Ordinal);
            var afterFirst = t.IndexOf('\n', first + 3);
            var last = t.LastIndexOf(fence, StringComparison.Ordinal);
            if (afterFirst > 0 && last > afterFirst)
                t = t[(afterFirst + 1)..last].Trim();
        }

        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start >= 0 && end > start)
            return t[start..(end + 1)];
        return t;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
