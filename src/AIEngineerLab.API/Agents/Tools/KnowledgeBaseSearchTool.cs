using System.Text.Json;

public sealed class KnowledgeBaseSearchTool : IAgentTool
{
    private static readonly RagSearchFilter TrustedScope =
        new("tenant-123", "US", 2026, null);

    private readonly IRagRetriever _ragRetriever;

    public KnowledgeBaseSearchTool(IRagRetriever ragRetriever)
    {
        _ragRetriever = ragRetriever;
    }

    public string Name => "search_knowledge_base";

    public string Description =>
        "Search the internal AI engineering knowledge base for grounded information. " +
        "Use this when the user's question depends on indexed internal documentation. " +
        "Authorization metadata is enforced by the backend and is not supplied by the model.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "A concise semantic search query for the internal knowledge base."
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

        var results = await _ragRetriever.SearchAsync(
            query,
            topK: 3,
            filter: TrustedScope,
            minimumSimilarity: 0,
            cancellationToken);

        return JsonSerializer.Serialize(results.Select(result => new
        {
            documentId = result.Document.Id,
            content = result.Document.Content,
            metadata = result.Document.Metadata,
            score = result.Score
        }));
    }
}
