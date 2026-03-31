namespace TravelDisruptionAgent.Api.Middleware;

/// <summary>
/// Baseline headers for JSON APIs (mitigates XSS / MIME sniffing; does not replace auth).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.Append("X-Content-Type-Options", "nosniff");
        headers.Append("X-Frame-Options", "DENY");
        headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
        // API returns JSON/SSE; Swagger UI needs a relaxed CSP on /swagger/* (Development only usage).
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/swagger") &&
            !headers.ContainsKey("Content-Security-Policy"))
            headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
        return _next(context);
    }
}
