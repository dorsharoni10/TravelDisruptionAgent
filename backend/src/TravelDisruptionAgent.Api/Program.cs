using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TravelDisruptionAgent.Api.Middleware;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Telemetry;
using TravelDisruptionAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.LogicalHandler", LogLevel.Warning);

var authSection = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
ValidateProductionConfiguration(builder.Environment, builder.Configuration, authSection);

if (authSection.Enabled)
{
    var secret = authSection.Jwt.SecretKey ?? "";
    if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        throw new InvalidOperationException(
            "Auth:Enabled is true but Auth:Jwt:SecretKey is missing or shorter than 32 characters. Set Auth__Jwt__SecretKey in the environment.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer = true,
                ValidIssuer = authSection.Jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = authSection.Jwt.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
}
else
{
    builder.Services.AddAuthorization();
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("chat", httpContext =>
    {
        var rl = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var key = RateLimitPartitionKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, rl.ChatPermitLimit),
                Window = TimeSpan.FromMinutes(Math.Max(1, rl.ChatWindowMinutes)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddPolicy("preferences", httpContext =>
    {
        var rl = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var key = RateLimitPartitionKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, rl.PreferencesPermitLimit),
                Window = TimeSpan.FromMinutes(Math.Max(1, rl.PreferencesWindowMinutes)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddPolicy("auth", httpContext =>
    {
        var rl = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"auth:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, rl.AuthTokenPermitLimit),
                Window = TimeSpan.FromMinutes(Math.Max(1, rl.AuthTokenWindowMinutes)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(options =>
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Travel Disruption Agent API", Version = "v1" }));
}

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
if (securityOptions.RequireHttpsRedirection)
{
    builder.Services.AddHttpsRedirection(options =>
    {
        options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
        options.HttpsPort = builder.Configuration.GetValue("Security:HttpsPort", 443);
    });
}

if (!builder.Environment.IsProduction())
{
    var llmKeyPresent = !string.IsNullOrWhiteSpace(builder.Configuration["Llm:ApiKey"]);
    Console.WriteLine(
        $"[Config] Llm Provider={builder.Configuration["Llm:Provider"] ?? "?"}, " +
        $"Model={builder.Configuration["Llm:Model"] ?? "?"}, ApiKeyConfigured={llmKeyPresent}");
    Console.WriteLine($"[Config] Auth:Enabled={authSection.Enabled}");
    Console.WriteLine($"[Config] Security:RequireHttpsRedirection={securityOptions.RequireHttpsRedirection}");
}

var otlpEndpoint = builder.Configuration["Otlp:Endpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "TravelDisruptionAgent",
            serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName
        }))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("TravelDisruptionAgent.Orchestrator")
            .AddSource(AgenticTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation(opts =>
            {
                opts.RecordException = true;
                opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                opts.EnrichWithHttpRequest = (activity, request) =>
                {
                    // Avoid storing query strings on incoming requests (secrets, tokens in URL).
                    var raw = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";
                    if (Uri.TryCreate(raw, UriKind.Absolute, out var u))
                    {
                        var redacted = HttpUriRedaction.WithoutQueryAndFragment(u);
                        var s = redacted?.ToString() ?? raw;
                        activity.SetTag("http.request.uri", s);
                        activity.SetTag("url.full", s);
                    }
                    else
                        activity.SetTag("http.request.uri", raw);
                };
            })
            .AddHttpClientInstrumentation(opts =>
            {
                opts.RecordException = true;
                opts.EnrichWithHttpRequestMessage = (activity, request) =>
                {
                    if (request.RequestUri is null) return;
                    var redacted = HttpUriRedaction.WithoutQueryAndFragment(request.RequestUri);
                    var s = redacted?.ToString() ?? "";
                    activity.SetTag("http.url", s);
                    activity.SetTag("url.full", s);
                };
            });

        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
            if (!builder.Environment.IsProduction())
                Console.WriteLine($"[OTel] OTLP exporter configured → {otlpEndpoint}");
        }
        else
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(AgenticTelemetry.MeterName);
        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
    });

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ragService = scope.ServiceProvider.GetRequiredService<IRagService>();
    await ragService.SeedAsync();
    var sessionStore = scope.ServiceProvider.GetRequiredService<IConversationSessionStore>();
    if (!app.Environment.IsProduction())
        Console.WriteLine($"[Config] Conversation session store: {sessionStore.GetType().Name}");
}

if (securityOptions.RequireHttpsRedirection)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseCors();

if (authSection.Enabled)
    app.UseAuthentication();

app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1"));
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

static void ValidateProductionConfiguration(
    IHostEnvironment environment,
    IConfiguration configuration,
    AuthOptions auth)
{
    if (!environment.IsProduction())
        return;

    var envVar = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    if (!auth.Enabled)
        throw new InvalidOperationException(
            "Production requires Auth:Enabled=true. Set Auth__Enabled=true and Auth__Jwt__SecretKey (32+ characters) in the environment.");

    if (auth.AllowDevTokenEndpoint)
        throw new InvalidOperationException(
            "Unsafe configuration: Auth:AllowDevTokenEndpoint must be false when the host environment is Production. " +
            "The dev JWT endpoint must never be enabled in production.");

    if (string.Equals(envVar, "Development", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "ASPNETCORE_ENVIRONMENT is set to Development while the host environment is Production. Fix deployment configuration.");

    var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    if (origins.Length == 0)
        throw new InvalidOperationException(
            "Production requires at least one AllowedOrigins entry (e.g. AllowedOrigins__0=https://your-spa.example). CORS uses credentials; wildcards are not allowed.");

    foreach (var origin in origins)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.Equals(origin.Trim(), "*", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Production CORS: each AllowedOrigins entry must be an explicit origin URL, not \"*\".");
    }

    var allowedHosts = configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) ||
        string.Equals(allowedHosts.Trim(), "*", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Production requires AllowedHosts to list your API hostname(s), not *. " +
            "Example: AllowedHosts=api.example.com or AllowedHosts=host1.com;host2.com");
}

static string RateLimitPartitionKey(HttpContext context)
{
    var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrEmpty(userId))
        return "user:" + userId;

    return "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}

public partial class Program { }
