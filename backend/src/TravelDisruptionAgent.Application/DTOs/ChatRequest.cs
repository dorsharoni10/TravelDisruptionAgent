using System.ComponentModel.DataAnnotations;
using TravelDisruptionAgent.Application.Validation;

namespace TravelDisruptionAgent.Application.DTOs;

/// <param name="SessionStorageNamespace">
/// Server-supplied tenant/user scope for persistence. When set (authenticated API), the storage key is
/// <c>namespace + U+001F + clientSessionId</c> so clients cannot read other users' sessions by reusing a guid.
/// Ignored from the client body — the API overwrites this from the JWT.
/// </param>
public record ChatRequest(
    [Required(ErrorMessage = "message is required")]
    [MinLength(1, ErrorMessage = "message must not be empty")]
    [MaxLength(ChatInputLimits.MaxMessageLength, ErrorMessage = "message exceeds maximum length")]
    string Message,
    string SessionId = "",
    string UserId = "default",
    string? SessionStorageNamespace = null
);
