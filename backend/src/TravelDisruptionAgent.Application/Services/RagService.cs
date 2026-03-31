using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelDisruptionAgent.Application.DTOs;
using TravelDisruptionAgent.Application.Interfaces;
using TravelDisruptionAgent.Application.Options;

namespace TravelDisruptionAgent.Application.Services;

public class RagService : IRagService
{
    private readonly List<PolicyDocument> _documents = [];
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmOptions _llmOptions;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RagService> _logger;
    private bool _embeddingsAvailable = true;

    /// <summary>Semantic anchors + one-line summaries aligned with seeded policy documents.</summary>
    private static readonly (string Phrase, string[] Topics)[] PolicyAnchorDefinitions =
    [
        ("company travel policy employee reimbursement expense rules and allowances", ["general-policy", "expense-limits"]),
        ("flight disruption compensation passenger rights claims and vouchers", ["compensation-rights"]),
        ("meals hotels meal vouchers per diem during travel delays and cancellations", ["expense-limits", "compensation-rights"]),
        ("baggage delayed lost luggage airline liability and claims", ["baggage-rules"]),
        ("rebooking change fees alternate flights after cancellation", ["rebooking-policy"]),
        ("rebooking same airline partner manager approval change fees disruption", ["rebooking-policy"]),
        ("hotel meal allowance ground transportation receipts expense limits disruption", ["expense-limits"]),
        ("airport arrival domestic international buffer holiday baggage check-in timing", ["general-policy", "checkin-guidance"]),
        ("online check-in counter closes bag drop mobile boarding pass", ["checkin-guidance"]),
        ("checked baggage drop delayed lost PIR Montreal Convention compensation", ["baggage-rules"]),
        ("EU261 US DOT denied boarding refund compensation extraordinary circumstances", ["compensation-rights"]),
        ("meeting buffer domestic international jet lag remote fallback schedule", ["meeting-buffer"]),
        ("weather advisory waiver alternative airport winter essentials hurricane safety", ["general-policy", "rebooking-policy"])
    ];

    private readonly SemaphoreSlim _policyAnchorLock = new(1, 1);
    private float[][]? _policyAnchorEmbeddings;
    private bool _policyAnchorInitFailed;

    /// <summary>Reuses query vectors for identical text within the process (policy hints + semantic RAG).</summary>
    private readonly ConcurrentDictionary<string, float[]> _queryEmbeddingCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RagService(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmOptions> llmOptions,
        IOptions<RagOptions> ragOptions,
        ILogger<RagService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _llmOptions = llmOptions.Value;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    public int DocumentCount => _documents.Count;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (_documents.Count > 0) return;

        _logger.LogInformation("Seeding RAG knowledge base...");

        var docs = GetSeedDocuments();

        if (_llmOptions.IsConfigured)
        {
            try
            {
                foreach (var (id, content) in docs)
                {
                    var embedding = await GenerateEmbeddingAsync(content, cancellationToken);
                    _documents.Add(new PolicyDocument(id, content, BuildKeywords(content), embedding));
                }

                _logger.LogInformation(
                    "RAG seeded with {Count} documents using semantic embeddings (model: {Model})",
                    _documents.Count, _llmOptions.EmbeddingModel);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Embedding generation failed during seed — falling back to keyword-based RAG");
                _embeddingsAvailable = false;
                _documents.Clear();
            }
        }
        else
        {
            _embeddingsAvailable = false;
            _logger.LogInformation("LLM API key not configured — using keyword-based RAG");
        }

        foreach (var (id, content) in docs)
            _documents.Add(new PolicyDocument(id, content, BuildKeywords(content), null));

        _logger.LogInformation("RAG seeded with {Count} documents (keyword-based fallback)", _documents.Count);
    }

    public async Task IndexDocumentAsync(
        string documentId, string content, CancellationToken cancellationToken = default)
    {
        float[]? embedding = null;
        if (_embeddingsAvailable)
        {
            try { embedding = await GenerateEmbeddingAsync(content, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Embedding failed for {DocId}", documentId); }
        }

        _documents.RemoveAll(d => d.Id == documentId);
        _documents.Add(new PolicyDocument(documentId, content, BuildKeywords(content), embedding));
        _logger.LogDebug("Indexed document: {DocumentId} (embedding={HasEmbedding})",
            documentId, embedding is not null);
    }

    public async Task<PolicyRagHints> GetPolicyRagHintsAsync(
        string userMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || !_llmOptions.IsConfigured)
            return new PolicyRagHints(false, 0, []);

        try
        {
            if (!await EnsurePolicyAnchorEmbeddingsAsync(cancellationToken))
                return new PolicyRagHints(false, 0, []);

            var queryEmbedding = await GetOrCreateQueryEmbeddingAsync(userMessage.Trim(), cancellationToken);
            double best = 0;
            var bestTopics = new[] { "general-policy" };
            for (var i = 0; i < _policyAnchorEmbeddings!.Length; i++)
            {
                var s = CosineSimilarity(queryEmbedding, _policyAnchorEmbeddings[i]);
                if (s > best)
                {
                    best = s;
                    bestTopics = PolicyAnchorDefinitions[i].Topics;
                }
            }

            var threshold = _ragOptions.PolicyRoutingMinSimilarity;
            var suggests = best >= threshold;
            _logger.LogDebug("Policy RAG hints: best similarity {Score:F3} (threshold {Threshold:F3})", best, threshold);

            var topics = suggests
                ? bestTopics.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
            if (suggests && !topics.Contains("general-policy", StringComparer.OrdinalIgnoreCase))
                topics.Insert(0, "general-policy");

            return new PolicyRagHints(suggests, best, topics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Policy RAG hints failed");
            return new PolicyRagHints(false, 0, []);
        }
    }

    public async Task<bool> MessageSuggestsPolicyKnowledgeAsync(
        string userMessage, CancellationToken cancellationToken = default)
    {
        var hints = await GetPolicyRagHintsAsync(userMessage, cancellationToken);
        return hints.SuggestsPolicy;
    }

    private async Task<bool> EnsurePolicyAnchorEmbeddingsAsync(CancellationToken cancellationToken)
    {
        if (_policyAnchorInitFailed)
            return false;
        if (_policyAnchorEmbeddings is not null)
            return true;

        await _policyAnchorLock.WaitAsync(cancellationToken);
        try
        {
            if (_policyAnchorEmbeddings is not null)
                return true;
            if (_policyAnchorInitFailed)
                return false;

            var list = new List<float[]>();
            foreach (var (phrase, _) in PolicyAnchorDefinitions)
                list.Add(await GenerateEmbeddingAsync(phrase, cancellationToken));

            _policyAnchorEmbeddings = list.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to embed policy anchor phrases — semantic routing gate disabled");
            _policyAnchorEmbeddings = null;
            _policyAnchorInitFailed = true;
            return false;
        }
        finally
        {
            _policyAnchorLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> RetrieveRelevantContextAsync(
        string query, int topK = 3, CancellationToken cancellationToken = default)
    {
        if (_documents.Count == 0)
        {
            _logger.LogDebug("RAG knowledge base is empty");
            return [];
        }

        if (_embeddingsAvailable && _documents.Any(d => d.Embedding is not null))
            return await SemanticSearchAsync(query, topK, cancellationToken);

        return KeywordSearch(query, topK);
    }

    public async Task<PolicyRetrievalDetail> RetrievePolicyKnowledgeDetailedAsync(
        string query, int topK = 3, CancellationToken cancellationToken = default)
    {
        if (_documents.Count == 0)
            return new PolicyRetrievalDetail([], null, 0);

        if (_embeddingsAvailable && _documents.Any(d => d.Embedding is not null))
            return await SemanticSearchDetailedAsync(query, topK, cancellationToken);

        return KeywordSearchDetailed(query, topK);
    }

    // ── Semantic search (embedding-based) ─────────────────────────────

    private async Task<IReadOnlyList<string>> SemanticSearchAsync(
        string query, int topK, CancellationToken ct)
    {
        var detail = await SemanticSearchDetailedAsync(query, topK, ct);
        return detail.Chunks.Select(c => c.Content).ToList();
    }

    private async Task<PolicyRetrievalDetail> SemanticSearchDetailedAsync(
        string query, int topK, CancellationToken ct)
    {
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await GetOrCreateQueryEmbeddingAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query embedding failed — falling back to keyword search");
            return KeywordSearchDetailed(query, topK);
        }

        var threshold = _ragOptions.DocumentRetrievalMinSimilarity;
        var ranked = _documents
            .Where(d => d.Embedding is not null)
            .Select(d => (d, score: CosineSimilarity(queryEmbedding, d.Embedding!)))
            .OrderByDescending(x => x.score)
            .ToList();

        var above = ranked.Count(x => x.score > threshold);
        var results = ranked
            .Where(x => x.score > threshold)
            .Take(topK)
            .ToList();

        var chunks = results
            .Select(x => new PolicyRetrievalChunk(
                x.d.Id,
                TruncateChunkForRetrieval(x.d.Content, _ragOptions.MaxChunkCharsForRetrieval),
                x.score))
            .ToList();

        double? bestSim = results.Count > 0 ? results[0].score : null;
        _logger.LogDebug("Semantic RAG: {Count} results (top score: {TopScore})",
            chunks.Count, bestSim?.ToString("F3") ?? "n/a");

        return new PolicyRetrievalDetail(chunks, bestSim, above);
    }

    // ── Keyword search (fallback) ─────────────────────────────────────

    private IReadOnlyList<string> KeywordSearch(string query, int topK) =>
        KeywordSearchDetailed(query, topK).Chunks.Select(c => c.Content).ToList();

    private PolicyRetrievalDetail KeywordSearchDetailed(string query, int topK)
    {
        var queryKeywords = BuildKeywords(query);

        var scored = _documents
            .Select(doc => (doc, score: ComputeKeywordRelevance(queryKeywords, doc.Keywords)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(topK)
            .ToList();

        var chunks = scored
            .Select(x => new PolicyRetrievalChunk(
                x.doc.Id,
                TruncateChunkForRetrieval(x.doc.Content, _ragOptions.MaxChunkCharsForRetrieval),
                x.score))
            .ToList();

        _logger.LogDebug("Keyword RAG fallback: {Count} results", chunks.Count);
        double? bestKw = scored.Count > 0 ? scored[0].score : null;
        return new PolicyRetrievalDetail(chunks, bestKw, scored.Count);
    }

    private static string TruncateChunkForRetrieval(string content, int maxChars)
    {
        if (maxChars <= 0 || content.Length <= maxChars)
            return content;
        return content.AsSpan(0, maxChars).TrimEnd().ToString() + "…";
    }

    private async Task<float[]> GetOrCreateQueryEmbeddingAsync(string query, CancellationToken ct)
    {
        var key = NormalizeQueryEmbeddingKey(query);
        if (_queryEmbeddingCache.TryGetValue(key, out var cached))
            return cached;

        var vec = await CallGeminiEmbeddingApiAsync(query, ct);

        if (_queryEmbeddingCache.Count >= 128)
            _queryEmbeddingCache.Clear();
        _queryEmbeddingCache[key] = vec;
        return vec;
    }

    private static string NormalizeQueryEmbeddingKey(string query)
    {
        var t = query.Trim();
        if (t.Length > 512)
            t = t[..512];
        return t.ToLowerInvariant();
    }

    // ── Gemini Embedding API ──────────────────────────────────────────

    /// <summary>Uncached — used for document seeding and indexing.</summary>
    private async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct) =>
        await CallGeminiEmbeddingApiAsync(text, ct);

    private async Task<float[]> CallGeminiEmbeddingApiAsync(string text, CancellationToken ct)
    {
        var model = _llmOptions.EmbeddingModel;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:embedContent?key={_llmOptions.ApiKey}";

        var requestBody = new
        {
            model = $"models/{model}",
            content = new
            {
                parts = new[] { new { text } }
            }
        };

        using var client = _httpClientFactory.CreateClient("RagEmbedding");
        using var response = await client.PostAsJsonAsync(url, requestBody, JsonOpts, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Gemini Embedding API returned {(int)response.StatusCode}: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbedContentResponse>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Empty response from Gemini Embedding API");

        return result.Embedding?.Values
            ?? throw new InvalidOperationException("Embedding response had no values");
    }

    // ── Math helpers ──────────────────────────────────────────────────

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dot / denom;
    }

    private static double ComputeKeywordRelevance(HashSet<string> queryTerms, HashSet<string> docTerms)
    {
        if (queryTerms.Count == 0 || docTerms.Count == 0) return 0;
        int overlap = queryTerms.Count(t => docTerms.Contains(t));
        return (double)overlap / queryTerms.Count;
    }

    private static HashSet<string> BuildKeywords(string text)
    {
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '.', ',', ':', ';', '!', '?', '(', ')', '-', '/', '—' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w));

        return new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }

    // ── Seed documents ────────────────────────────────────────────────

    private static List<(string Id, string Content)> GetSeedDocuments() =>
    [
        ("policy-rebooking", """
            Company Travel Policy — Rebooking Rules:
            Employees may rebook on the same airline within 24 hours of a cancellation at no additional cost.
            If no same-airline option is available within 24 hours, rebooking on a partner airline is permitted with manager approval.
            Change fees up to $200 are covered for disruption-related changes.
            Business class upgrades are authorized if the delay exceeds 4 hours and economy alternatives are not available within 2 hours.
            All rebooking must be documented with the original booking reference and disruption reason.
            """),

        ("policy-expenses", """
            Company Travel Policy — Disruption Expense Limits:
            Hotel accommodation during disruption: up to $200 per night, maximum 3 nights.
            Meal allowance during disruption: up to $50 per day ($15 breakfast, $15 lunch, $20 dinner).
            Ground transportation (taxi/rideshare) to alternative airport: up to $100 per trip.
            Phone/internet charges for rebooking: up to $25 per incident.
            All expenses must be submitted with receipts within 14 days of return.
            Travel insurance covers cancellations due to weather, strikes, and medical emergencies.
            """),

        ("policy-arrival", """
            Airport Arrival Recommendations:
            Domestic flights: arrive at least 2 hours before scheduled departure.
            International flights: arrive at least 3 hours before scheduled departure.
            During known weather events or peak travel periods: add 1 additional hour.
            Holiday periods (Thanksgiving, Christmas, Summer peak): add 30 minutes to standard recommendation.
            If checking oversized or special baggage: add 30 minutes.
            TSA PreCheck / Global Entry members may reduce buffer by 30 minutes for security, but not for check-in.
            """),

        ("policy-checkin", """
            Check-in Guidance:
            Online check-in typically opens 24 hours before departure and closes 1 hour before.
            Airport counter check-in closes 45 minutes before departure for domestic flights.
            Airport counter check-in closes 60 minutes before departure for international flights.
            Bag drop counters close 45 minutes before departure regardless of flight type.
            If you miss the check-in window, contact the airline immediately — some allow late check-in at the gate for a fee.
            Mobile boarding passes are accepted by most airlines; always have a backup screenshot.
            """),

        ("policy-baggage", """
            Baggage Timing and Rules:
            Checked baggage must be dropped at least 45 minutes before departure.
            Oversized or special items (sports equipment, instruments): drop at least 60 minutes before departure.
            Delayed baggage: file a Property Irregularity Report (PIR) at the airport before leaving.
            Delayed baggage claims must be filed within 21 days of receiving the bag.
            Lost baggage compensation: up to $3,800 per passenger under the Montreal Convention for international flights.
            Domestic lost baggage: airline-specific limits apply, typically $3,500–$3,800.
            Keep receipts for essential items purchased due to baggage delay — most airlines reimburse reasonable expenses.
            """),

        ("policy-compensation", """
            Disruption Compensation Rights:
            EU Regulation 261/2004 (EU261): Passengers on EU-departing or EU-airline flights are entitled to 250–600 EUR compensation for cancellations and long delays (3+ hours), unless caused by extraordinary circumstances (severe weather, security).
            US DOT Rules: Airlines must provide a full refund for cancelled flights. No mandatory compensation for delays, but airlines often offer vouchers.
            Denied boarding (overbooking): Compensation of 200–400% of the one-way fare, depending on delay length and regulations.
            Always request written confirmation of the disruption reason from the airline.
            Keep all boarding passes and receipts for compensation claims.
            """),

        ("policy-meeting-buffer", """
            Meeting and Schedule Buffer Guidelines:
            Minimum buffer between flight arrival and first meeting: 2 hours for domestic, 3 hours for international.
            Same-day meetings after international flights are not recommended — jet lag affects performance.
            Factor in ground transportation time: 30–90 minutes depending on airport and city.
            For critical meetings, consider arriving the evening before.
            If your flight is delayed and the meeting buffer is less than 1 hour, consider rescheduling or joining remotely.
            Always have a remote fallback plan (video conference link) for important meetings.
            """),

        ("policy-weather", """
            Weather Disruption Procedures:
            Monitor weather forecasts 48 hours before travel.
            If a weather advisory is issued for departure or arrival airport, contact the airline proactively for rebooking options.
            Most airlines issue weather waivers that allow free rebooking during significant weather events.
            Consider alternative airports within 100 miles of your destination during weather disruptions.
            Winter weather: carry essentials (charger, medications, snacks) in carry-on in case of extended delays.
            Hurricane/tornado warnings: do not travel — safety takes priority over schedule.
            """)
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can", "her", "was",
        "one", "our", "out", "has", "his", "how", "its", "may", "new", "now", "old",
        "see", "way", "who", "did", "get", "him", "let", "say", "she", "too", "use",
        "with", "this", "that", "from", "have", "been", "will", "your", "they", "more",
        "some", "than", "other", "into", "could", "would", "about", "after", "should",
        "also", "each", "just", "most", "must", "before", "between", "during", "within"
    };

    private sealed record PolicyDocument(
        string Id, string Content, HashSet<string> Keywords, float[]? Embedding);

    // ── Gemini response DTOs ──────────────────────────────────────────

    private sealed class EmbedContentResponse
    {
        public EmbeddingPayload? Embedding { get; set; }
    }

    private sealed class EmbeddingPayload
    {
        public float[]? Values { get; set; }
    }
}
