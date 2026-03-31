using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Validation;
using TravelDisruptionAgent.Domain.Entities;

namespace TravelDisruptionAgent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PreferencesController : ControllerBase
{
    /// <summary>Optional when <c>Auth:Enabled</c> is false: stable per-browser id (1–64 chars, [a-zA-Z0-9_-]). Not a security boundary.</summary>
    public const string AnonymousUserIdHeader = "X-Anonymous-User-Id";

    private static readonly Regex AnonymousPreferencesIdRegex = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    private readonly IMemoryService _memoryService;
    private readonly ILogger<PreferencesController> _logger;
    private readonly AuthOptions _auth;

    public PreferencesController(
        IMemoryService memoryService,
        ILogger<PreferencesController> logger,
        IOptions<AuthOptions> authOptions)
    {
        _memoryService = memoryService;
        _logger = logger;
        _auth = authOptions.Value;
    }

    [HttpGet]
    [EnableRateLimiting("preferences")]
    public async Task<ActionResult<UserPreferences>> Get(CancellationToken cancellationToken)
    {
        var denied = TryResolvePreferencesUserId(out var userId);
        if (denied != null)
            return denied;
        var prefs = await _memoryService.GetPreferencesAsync(userId, cancellationToken);
        return Ok(prefs);
    }

    [HttpPut]
    [EnableRateLimiting("preferences")]
    public async Task<IActionResult> Put([FromBody] UserPreferencesDto dto, CancellationToken cancellationToken)
    {
        var denied = TryResolvePreferencesUserId(out var userId);
        if (denied != null)
            return denied;

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        foreach (var (field, messages) in UserPreferencesValidator.GetErrors(dto))
        {
            foreach (var message in messages)
                ModelState.AddModelError(field, message);
        }

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var airports = (dto.PreferredAirports ?? [])
            .Select(a => a.Trim().ToUpperInvariant())
            .Where(a => a.Length > 0)
            .ToList();

        var prefs = new UserPreferences
        {
            UserId = userId,
            PreferredAirline = dto.PreferredAirline?.Trim() ?? "",
            SeatPreference = dto.SeatPreference?.Trim() ?? "",
            MealPreference = dto.MealPreference?.Trim() ?? "",
            LoyaltyProgram = dto.LoyaltyProgram?.Trim() ?? "",
            MaxLayovers = dto.MaxLayovers,
            MaxBudgetUsd = dto.MaxBudgetUsd,
            PreferredAirports = airports,
            HomeAirport = (dto.HomeAirport ?? "").Trim().ToUpperInvariant(),
            RiskTolerance = UserPreferencesValidator.NormalizeRiskTolerance(dto.RiskTolerance ?? ""),
            PrefersRemoteFallback = dto.PrefersRemoteFallback,
            PreferredMeetingBufferMinutes = dto.PreferredMeetingBufferMinutes
        };

        await _memoryService.SavePreferencesAsync(prefs, cancellationToken);
        _logger.LogInformation("Preferences updated for user {UserId}", userId);

        return NoContent();
    }

    private ActionResult? TryResolvePreferencesUserId(out string userId)
    {
        if (!_auth.Enabled)
        {
            var anon = Request.Headers[AnonymousUserIdHeader].FirstOrDefault();
            if (!string.IsNullOrEmpty(anon) && AnonymousPreferencesIdRegex.IsMatch(anon))
                userId = "anon:" + anon;
            else
                userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "default";
            return null;
        }

        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            userId = "";
            return Unauthorized();
        }

        userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        return null;
    }
}
