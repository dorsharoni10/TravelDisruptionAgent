namespace TravelDisruptionAgent.Application.DTOs;

public record DevTokenRequest(string Sub, string? Name = null);

public record DevTokenResponse(string AccessToken, string TokenType, int ExpiresInSeconds);
