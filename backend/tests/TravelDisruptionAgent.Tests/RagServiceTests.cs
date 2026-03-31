using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.Options;
using TravelDisruptionAgent.Application.Services;
using Xunit;

namespace TravelDisruptionAgent.Tests;

public class RagServiceTests
{
    private readonly RagService _service;

    public RagServiceTests()
    {
        _service = CreateKeywordOnlyRagService();
    }

    private static RagService CreateKeywordOnlyRagService()
    {
        var httpFactory = new NoOpHttpClientFactory();
        var llmOpts = Options.Create(new LlmOptions()); // ApiKey empty → keyword fallback
        var ragOpts = Options.Create(new RagOptions());
        return new RagService(httpFactory, llmOpts, ragOpts, NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task MessageSuggestsPolicyKnowledge_WithoutApiKey_ReturnsFalse()
    {
        var result = await _service.MessageSuggestsPolicyKnowledgeAsync(
            "Does my employer cover meals during a disruption?");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Seed_ShouldLoadDocuments()
    {
        await _service.SeedAsync();
        _service.DocumentCount.Should().BeGreaterOrEqualTo(6);
    }

    [Fact]
    public async Task Seed_CalledTwice_ShouldNotDuplicate()
    {
        await _service.SeedAsync();
        var count1 = _service.DocumentCount;
        await _service.SeedAsync();
        _service.DocumentCount.Should().Be(count1);
    }

    [Fact]
    public async Task Retrieve_CompensationQuery_ShouldFindRelevantDocs()
    {
        await _service.SeedAsync();
        var results = await _service.RetrieveRelevantContextAsync("compensation rights EU261");
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Contains("compensation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Retrieve_BaggageQuery_ShouldFindBaggageDocs()
    {
        await _service.SeedAsync();
        var results = await _service.RetrieveRelevantContextAsync("baggage delayed lost luggage");
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Contains("baggage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Retrieve_CheckinQuery_ShouldFindCheckinDocs()
    {
        await _service.SeedAsync();
        var results = await _service.RetrieveRelevantContextAsync("check-in counter closes when");
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Retrieve_UnrelatedQuery_ShouldReturnFewOrNoResults()
    {
        await _service.SeedAsync();
        var results = await _service.RetrieveRelevantContextAsync("quantum physics equations");
        results.Count.Should().BeLessOrEqualTo(1);
    }

    [Fact]
    public async Task Retrieve_EmptyKnowledgeBase_ShouldReturnEmpty()
    {
        var emptyService = CreateKeywordOnlyRagService();
        var results = await emptyService.RetrieveRelevantContextAsync("compensation rights");
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDocument_ShouldBeRetrievable()
    {
        await _service.SeedAsync();
        var countBefore = _service.DocumentCount;

        await _service.IndexDocumentAsync("custom-doc",
            "Custom policy: All employees get free lounge access during disruptions.");

        _service.DocumentCount.Should().Be(countBefore + 1);

        var results = await _service.RetrieveRelevantContextAsync("lounge access disruptions");
        results.Should().Contain(r => r.Contains("lounge", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class NoOpHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
