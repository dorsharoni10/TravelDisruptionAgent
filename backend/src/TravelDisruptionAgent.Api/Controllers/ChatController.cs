using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelDisruptionAgent.Application.Validation;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Api.Security;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    public const string SseStreamErrorEvent = "stream_error";

    private readonly IAgentOrchestrator _orchestrator;
    private readonly IMemoryService _memoryService;
    private readonly ILogger<ChatController> _logger;
    private readonly AuthOptions _authOptions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(
        IAgentOrchestrator orchestrator,
        IMemoryService memoryService,
        ILogger<ChatController> logger,
        IOptions<AuthOptions> authOptions)
    {
        _orchestrator = orchestrator;
        _memoryService = memoryService;
        _logger = logger;
        _authOptions = authOptions.Value;
    }

    /// <summary>Max JSON body size for chat (message + metadata). Independent of <see cref="ChatInputLimits.MaxMessageLength"/>.</summary>
    private const int ChatStreamMaxRequestBytes = 384 * 1024;

    [HttpPost("stream")]
    [EnableRateLimiting("chat")]
    [RequestSizeLimit(ChatStreamMaxRequestBytes)]
    public async Task Stream([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SSE stream request received");

        if (!TryBuildEffectiveChatRequest(request, out var effectiveRequest) || effectiveRequest is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            var response = await _orchestrator.ProcessAsync(
                effectiveRequest,
                async evt =>
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(evt, JsonOptions);
                        await Response.WriteAsync($"event: agent_event\ndata: {json}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to write agent_event to SSE stream; continuing pipeline");
                    }
                },
                cancellationToken);

            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            await Response.WriteAsync($"event: agent_response\ndata: {responseJson}\n\n", cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE stream cancelled by client or host");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat SSE stream failed before completion");
            await TryWriteStreamErrorAndDoneAsync(cancellationToken);
        }
    }

    /// <summary>
    /// When auth is enabled, requires <see cref="ClaimTypes.NameIdentifier"/> (same as <c>DELETE session</c>).
    /// </summary>
    private bool TryBuildEffectiveChatRequest(ChatRequest body, out ChatRequest? effectiveRequest)
    {
        if (!_authOptions.Enabled)
        {
            effectiveRequest = body with { SessionStorageNamespace = null };
            return true;
        }

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
        {
            effectiveRequest = null;
            return false;
        }

        effectiveRequest = body with
        {
            SessionStorageNamespace = sub,
            UserId = sub
        };
        return true;
    }

    private async Task TryWriteStreamErrorAndDoneAsync(CancellationToken cancellationToken)
    {
        var payload = new StreamErrorSse(
            Code: "stream_failed",
            Message: "The assistant hit an error. Please try again.");
        var errJson = JsonSerializer.Serialize(payload, JsonOptions);

        try
        {
            await Response.WriteAsync($"event: {SseStreamErrorEvent}\ndata: {errJson}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write stream_error event to client");
        }

        try
        {
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write SSE [DONE] after error");
        }
    }

    /// <summary>Remove conversation turns for a session (scoped to the authenticated user when auth is enabled).</summary>
    [HttpDelete("session")]
    [EnableRateLimiting("chat")]
    public async Task<IActionResult> ClearSession(
        [FromBody] ClearSessionRequest? body,
        CancellationToken cancellationToken)
    {
        var sessionId = body?.SessionId?.Trim();
        if (string.IsNullOrEmpty(sessionId))
            return BadRequest(new { error = "sessionId is required" });

        if (_authOptions.Enabled)
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(sub))
                return Unauthorized();
        }

        var storageKey = SessionStorageKeyBuilder.Build(_authOptions.Enabled, User, sessionId);
        await _memoryService.ClearConversationAsync(storageKey, cancellationToken);
        _logger.LogInformation("Cleared conversation session {SessionId}", sessionId);
        return NoContent();
    }
}
