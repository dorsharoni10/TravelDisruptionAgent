using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using TravelDisruptionAgent.Domain.Interfaces;
using TravelDisruptionAgent.Infrastructure.Providers;
using TravelDisruptionAgent.Infrastructure.SemanticKernel;
using TravelDisruptionAgent.Infrastructure.Services;

namespace TravelDisruptionAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AgentToolExecutionPipeline>();
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IAgenticToolExecutor, AgenticToolExecutor>();
        services.AddScoped<IAgentLoopService, AgentLoopService>();
        services.AddScoped<IConversationRouter, ConversationRouter>();
        services.AddScoped<IPlanningService, PlanningService>();
        services.AddScoped<IGuardrailsService, GuardrailsService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<ISelfCorrectionService, SelfCorrectionService>();
        services.AddSingleton<IMemoryService, MemoryService>();

        return services;
    }

    /// <param name="configuration">Host <c>IConfiguration</c> (appsettings, environment variables, command line, user secrets when configured on the host, etc.).</param>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<RagOptions>(configuration.GetSection(RagOptions.SectionName));
        services.Configure<AgenticOptions>(configuration.GetSection(AgenticOptions.SectionName));
        services.Configure<WeatherApiOptions>(configuration.GetSection(WeatherApiOptions.SectionName));
        services.Configure<AviationStackOptions>(configuration.GetSection(AviationStackOptions.SectionName));
        services.Configure<ConversationSessionOptions>(configuration.GetSection(ConversationSessionOptions.SectionName));
        services.Configure<OrchestrationOptions>(configuration.GetSection(OrchestrationOptions.SectionName));
        services.Configure<MongoDbOptions>(configuration.GetSection(MongoDbOptions.SectionName));
        var mongoConn = configuration.GetSection(MongoDbOptions.SectionName).Get<MongoDbOptions>()?.ConnectionString;
        if (!string.IsNullOrWhiteSpace(mongoConn))
        {
            services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConn));
            services.AddSingleton<IConversationSessionStore, MongoConversationSessionStore>();
        }
        else
        {
            services.AddSingleton<IConversationSessionStore, InMemoryConversationSessionStore>();
        }

        services.AddSingleton<IKernelFactory, SemanticKernelFactory>();
        services.AddScoped<IAgentLoopStepLlmInvoker, KernelAgentLoopStepLlmInvoker>();

        services.AddHttpClient("RagEmbedding", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IRagService, RagService>();

        services.AddScoped<IToolExecutionCoordinator, ToolExecutionCoordinator>();

        services.AddSingleton<MockWeatherProvider>();
        services.AddSingleton<MockFlightProvider>();

        // Weather provider: auto-select real vs mock based on config
        var weatherOptions = configuration.GetSection(WeatherApiOptions.SectionName).Get<WeatherApiOptions>();
        bool useRealWeather = weatherOptions is not null
            && !string.IsNullOrWhiteSpace(weatherOptions.ApiKey)
            && !weatherOptions.UseMock;

        if (useRealWeather)
        {
            services.AddHttpClient<WeatherApiProvider>(client =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(weatherOptions!.BaseUrl));
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<IWeatherProvider>(sp => sp.GetRequiredService<WeatherApiProvider>());
        }
        else
        {
            services.AddSingleton<IWeatherProvider>(sp => sp.GetRequiredService<MockWeatherProvider>());
        }

        // Flight provider: auto-select real vs mock based on config
        var aviationOptions = configuration.GetSection(AviationStackOptions.SectionName).Get<AviationStackOptions>();
        bool useRealFlights = aviationOptions is not null
            && !string.IsNullOrWhiteSpace(aviationOptions.ApiKey)
            && !aviationOptions.UseMock;

        if (useRealFlights)
        {
            services.AddHttpClient<AviationStackFlightProvider>(client =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(aviationOptions!.BaseUrl));
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddScoped<IFlightProvider>(sp => sp.GetRequiredService<AviationStackFlightProvider>());
        }
        else
        {
            services.AddSingleton<IFlightProvider>(sp => sp.GetRequiredService<MockFlightProvider>());
        }

        return services;
    }

    private static string EnsureTrailingSlash(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return baseUrl;

        return baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/";
    }
}
