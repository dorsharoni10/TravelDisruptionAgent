using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace TravelDisruptionAgent.Tests.Integration;

/// <summary>Integration tests use environment <c>Testing</c> and <c>appsettings.Testing.json</c> (in-memory Mongo, HTTPS off).</summary>
public class ApiIntegrationTests
{
    private const string TestEnv = "Testing";

    /// <summary>
    /// Merges keys into host configuration in <see cref="WebApplicationFactory{TEntryPoint}.CreateHost"/> so they are visible when
    /// <c>Program.cs</c> reads configuration at startup (factory <c>ConfigureAppConfiguration</c> runs too late for that).
    /// </summary>
    private sealed class HostConfigWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly IReadOnlyDictionary<string, string?>? _hostKeys;

        public HostConfigWebApplicationFactory(string environment, IReadOnlyDictionary<string, string?>? hostKeys)
        {
            _environment = environment;
            _hostKeys = hostKeys;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(_environment);

        protected override IHost CreateHost(IHostBuilder builder)
        {
            if (_hostKeys is { Count: > 0 })
            {
                builder.ConfigureHostConfiguration(config =>
                {
                    config.AddInMemoryCollection(_hostKeys);
                });
            }

            return base.CreateHost(builder);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        Action<Dictionary<string, string?>>? extraHostKeys = null)
    {
        var hostKeys = new Dictionary<string, string?>();
        extraHostKeys?.Invoke(hostKeys);
        return new HostConfigWebApplicationFactory(
            environment,
            hostKeys.Count > 0 ? hostKeys : null);
    }

    [Fact]
    public void Host_fails_to_start_when_Production_and_AllowDevTokenEndpoint_env_is_true()
    {
        var prev = Environment.GetEnvironmentVariable("Auth__AllowDevTokenEndpoint");
        Environment.SetEnvironmentVariable("Auth__AllowDevTokenEndpoint", "true");
        try
        {
            using var factory = new HostConfigWebApplicationFactory(Environments.Production, new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "",
                ["Security:RequireHttpsRedirection"] = "false",
                ["Auth:Enabled"] = "true",
                ["Auth:Jwt:SecretKey"] = new string('x', 40),
                ["AllowedOrigins:0"] = "http://localhost",
                ["AllowedHosts"] = "localhost"
            });

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("AllowDevTokenEndpoint", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (prev is null)
                Environment.SetEnvironmentVariable("Auth__AllowDevTokenEndpoint", null);
            else
                Environment.SetEnvironmentVariable("Auth__AllowDevTokenEndpoint", prev);
        }
    }

    [Fact]
    public async Task Health_returns_200_anonymously()
    {
        await using var factory = CreateFactory(TestEnv);
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Chat_stream_rejects_empty_message_with_400()
    {
        await using var factory = CreateFactory(TestEnv, d => d["Auth:Enabled"] = "false");
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat/stream", new { message = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Chat_stream_rejects_oversized_message_with_400()
    {
        await using var factory = CreateFactory(TestEnv, d => d["Auth:Enabled"] = "false");
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/chat/stream",
            new { message = new string('x', 32_001) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Chat_stream_returns_401_when_jwt_valid_but_missing_nameidentifier()
    {
        const string secret = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await using var factory = CreateFactory(TestEnv, d =>
        {
            d["Auth:Enabled"] = "true";
            d["Auth:Jwt:SecretKey"] = secret;
        });
        var client = factory.CreateClient();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "TravelDisruptionAgent",
            audience: "TravelDisruptionAgent.Spa",
            claims: [new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))],
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: creds);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var response = await client.PostAsJsonAsync("/api/chat/stream", new { message = "hello" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preferences_require_authorization_when_auth_enabled()
    {
        await using var factory = CreateFactory(TestEnv, d =>
        {
            d["Auth:Enabled"] = "true";
            d["Auth:Jwt:SecretKey"] = new string('k', 40);
        });
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/preferences");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dev_token_returns_404_in_Production_when_host_is_valid()
    {
        await using var factory = CreateFactory(Environments.Production, d =>
        {
            d["MongoDb:ConnectionString"] = "";
            d["Security:RequireHttpsRedirection"] = "false";
            d["Auth:Enabled"] = "true";
            d["Auth:AllowDevTokenEndpoint"] = "false";
            d["Auth:Jwt:SecretKey"] = new string('p', 40);
            d["AllowedOrigins:0"] = "http://localhost";
            d["AllowedHosts"] = "localhost";
        });
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new { sub = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dev_token_works_in_Development_when_auth_enabled_and_flag_on()
    {
        await using var factory = CreateFactory(Environments.Development, d =>
        {
            d["MongoDb:ConnectionString"] = "";
            d["Security:RequireHttpsRedirection"] = "false";
            d["Auth:Enabled"] = "true";
            d["Auth:AllowDevTokenEndpoint"] = "true";
            d["Auth:Jwt:SecretKey"] = new string('d', 40);
        });
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new { sub = "int-test-user" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DevTokenPayload>();
        Assert.NotNull(body?.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
    }

    private sealed record DevTokenPayload(string AccessToken, string TokenType, int ExpiresInSeconds);
}
