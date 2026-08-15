using System.Text.RegularExpressions;

public sealed class AdvancedRagPipeline
{
    private const double RrfK = 60;

    private readonly ILlmService _llmService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;

    public AdvancedRagPipeline(
        ILlmService llmService,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore)
    {
        _llmService = llmService;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
    }

    public async Task<AdvancedRagSearchResult> SearchAsync(
        AdvancedRagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Query is required.", nameof(request));

        var candidateTopK = Math.Clamp(request.CandidateTopK, 1, 50);
        var finalTopK = Math.Clamp(request.FinalTopK, 1, candidateTopK);
        var minimumVectorSimilarity = Math.Clamp(request.MinimumVectorSimilarity, -1, 1);
        var minimumRerankScore = Math.Clamp(request.MinimumRerankScore, 0, 1);

        var filter = new RagSearchFilter(
            request.TenantId,
            request.Country,
            request.Year,
            request.Department);

        var rewrite = await RewriteQueryAsync(request, cancellationToken);
        var searchQuery = rewrite.Query;

        var queryEmbeddingTask = _embeddingService.EmbedAsync(searchQuery, cancellationToken);
        var keywordResults = _vectorStore.KeywordSearch(searchQuery, candidateTopK, filter);
        var queryEmbedding = await queryEmbeddingTask;
        var vectorResults = _vectorStore.Search(
            queryEmbedding,
            candidateTopK,
            filter,
            minimumVectorSimilarity);

        var fused = Fuse(vectorResults, keywordResults, searchQuery);
        var finalResults = fused
            .Where(candidate => candidate.RerankScore >= minimumRerankScore)
            .OrderByDescending(candidate => candidate.RerankScore)
            .ThenByDescending(candidate => candidate.RrfScore)
            .Take(finalTopK)
            .ToList();

        return new AdvancedRagSearchResult(
            request.Query,
            searchQuery,
            rewrite.WasRewritten,
            rewrite.InputTokens,
            rewrite.OutputTokens,
            filter,
            _vectorStore.Count,
            _vectorStore.CountEligible(filter),
            vectorResults,
            keywordResults,
            fused,
            finalResults,
            candidateTopK,
            finalTopK,
            minimumVectorSimilarity,
            minimumRerankScore);
    }

    private async Task<(string Query, bool WasRewritten, int InputTokens, int OutputTokens)> RewriteQueryAsync(
        AdvancedRagSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.RewriteQuery ||
            (string.IsNullOrWhiteSpace(request.PreviousUserMessage) &&
             string.IsNullOrWhiteSpace(request.PreviousAssistantMessage)))
        {
            return (request.Query.Trim(), false, 0, 0);
        }

        var messages = new List<LlmMessage>
        {
            new("system",
                "Rewrite the latest user question into one concise standalone retrieval query. " +
                "Use only the recent conversation context needed to resolve pronouns or omitted subjects. " +
                "Do not answer the question. Return only the rewritten search query with no explanation."),
            new("user",
                $"Previous user message: {request.PreviousUserMessage ?? "(none)"}\n" +
                $"Previous assistant message: {request.PreviousAssistantMessage ?? "(none)"}\n" +
                $"Latest user question: {request.Query}")
        };

        var generation = await _llmService.GenerateAsync(
            messages,
            Array.Empty<LlmToolDefinition>(),
            cancellationToken);

        var rewritten = generation.Text
            .Trim()
            .Trim('"', '\'', '`');

        if (string.IsNullOrWhiteSpace(rewritten))
            rewritten = request.Query.Trim();

        return (
            rewritten,
            !string.Equals(rewritten, request.Query.Trim(), StringComparison.OrdinalIgnoreCase),
            generation.InputTokens,
            generation.OutputTokens);
    }

    private static IReadOnlyList<AdvancedRagCandidate> Fuse(
        IReadOnlyList<RagSearchResult> vectorResults,
        IReadOnlyList<RagSearchResult> keywordResults,
        string query)
    {
        var candidates = new Dictionary<string, CandidateBuilder>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < vectorResults.Count; index++)
        {
            var result = vectorResults[index];
            var builder = GetBuilder(candidates, result.Document);
            builder.VectorRank = index + 1;
            builder.VectorScore = result.Score;
            builder.RrfScore += 1.0 / (RrfK + index + 1);
        }

        for (var index = 0; index < keywordResults.Count; index++)
        {
            var result = keywordResults[index];
            var builder = GetBuilder(candidates, result.Document);
            builder.KeywordRank = index + 1;
            builder.KeywordScore = result.Score;
            builder.RrfScore += 1.0 / (RrfK + index + 1);
        }

        var maxRrf = Math.Max(candidates.Values.Select(candidate => candidate.RrfScore).DefaultIfEmpty(1).Max(), 0.000001);
        var maxKeyword = Math.Max(candidates.Values.Select(candidate => candidate.KeywordScore ?? 0).DefaultIfEmpty(1).Max(), 0.000001);

        return candidates.Values
            .Select(candidate =>
            {
                var semantic = Math.Clamp(candidate.VectorScore ?? 0, 0, 1);
                var lexical = Math.Clamp((candidate.KeywordScore ?? 0) / maxKeyword, 0, 1);
                var fusedRank = Math.Clamp(candidate.RrfScore / maxRrf, 0, 1);
                var answerAlignment = QueryCoverage(query, candidate.Document.Content);

                // Educational reranker: combines independent retrieval evidence with
                // direct query/content alignment. Replace this component with a
                // dedicated cross-encoder/reranking model in a production stack.
                var rerank = Math.Clamp(
                    (semantic * 0.35) +
                    (lexical * 0.20) +
                    (fusedRank * 0.20) +
                    (answerAlignment * 0.25),
                    0,
                    1);

                return new AdvancedRagCandidate(
                    candidate.Document,
                    candidate.VectorRank,
                    candidate.VectorScore,
                    candidate.KeywordRank,
                    candidate.KeywordScore,
                    candidate.RrfScore,
                    rerank);
            })
            .OrderByDescending(candidate => candidate.RerankScore)
            .ThenByDescending(candidate => candidate.RrfScore)
            .ToList();
    }

    private static CandidateBuilder GetBuilder(
        IDictionary<string, CandidateBuilder> candidates,
        RagDocument document)
    {
        if (!candidates.TryGetValue(document.Id, out var builder))
        {
            builder = new CandidateBuilder(document);
            candidates[document.Id] = builder;
        }

        return builder;
    }

    private static double QueryCoverage(string query, string content)
    {
        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
            return 0;

        var contentTerms = Tokenize(content);
        var matched = queryTerms.Count(term => contentTerms.Contains(term));
        var coverage = (double)matched / queryTerms.Count;

        var identifierTerms = queryTerms.Where(LooksLikeIdentifier).ToList();
        if (identifierTerms.Count > 0 && identifierTerms.All(term => contentTerms.Contains(term)))
            coverage = Math.Min(1, coverage + 0.2);

        return coverage;
    }

    private static HashSet<string> Tokenize(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9_-]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeIdentifier(string value) =>
        value.Any(char.IsDigit) || value.Contains('_') || value.Contains('-');

    private sealed class CandidateBuilder
    {
        public CandidateBuilder(RagDocument document) => Document = document;

        public RagDocument Document { get; }
        public int? VectorRank { get; set; }
        public double? VectorScore { get; set; }
        public int? KeywordRank { get; set; }
        public double? KeywordScore { get; set; }
        public double RrfScore { get; set; }
    }
}
