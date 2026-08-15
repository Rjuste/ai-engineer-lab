using System.Text.Json;

public sealed class KnowledgeBaseSearchTool : IAgentTool
{
    private readonly AdvancedRagPipeline _pipeline;

    public KnowledgeBaseSearchTool(AdvancedRagPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public string Name => "search_knowledge_base";

    public string Description =>
        "Search the authorized internal knowledge base using metadata filtering, hybrid semantic/keyword retrieval, fusion, and reranking. " +
        "Use this when the user's question depends on indexed internal documentation. " +
        "Authorization metadata is enforced by the backend and is never supplied by the model.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "A concise standalone search query for the internal knowledge base."
            }
        },
        required = new[] { "query" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        if (!arguments.RootElement.TryGetProperty("query", out var queryElement))
            throw new ArgumentException("Tool argument 'query' is required.");

        var query = queryElement.GetString();

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Tool argument 'query' cannot be empty.");

        // The orchestrating LLM already supplied a standalone tool query, so the tool
        // skips a second LLM rewrite and goes directly into the retrieval funnel.
        var result = await _pipeline.SearchAsync(
            new AdvancedRagSearchRequest(
                Query: query,
                RewriteQuery: false,
                CandidateTopK: 20,
                FinalTopK: 3,
                MinimumVectorSimilarity: 0,
                MinimumRerankScore: 0.35,
                TenantId: "tenant-123",
                Country: "US",
                Year: 2026),
            cancellationToken);

        return JsonSerializer.Serialize(new
        {
            searchQuery = result.SearchQuery,
            eligibleVectorCount = result.EligibleVectorCount,
            vectorCandidateCount = result.VectorResults.Count,
            keywordCandidateCount = result.KeywordResults.Count,
            fusedCandidateCount = result.FusedCandidates.Count,
            results = result.FinalResults.Select(candidate => new
            {
                documentId = candidate.Document.Id,
                content = candidate.Document.Content,
                metadata = candidate.Document.Metadata,
                candidate.RerankScore,
                candidate.RrfScore,
                candidate.VectorScore,
                candidate.KeywordScore
            })
        });
    }
}
