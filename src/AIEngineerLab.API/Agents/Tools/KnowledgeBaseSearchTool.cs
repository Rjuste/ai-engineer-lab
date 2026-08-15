public sealed class KnowledgeBaseSearchTool
{
    private readonly IRagRetriever _ragRetriever;

    public KnowledgeBaseSearchTool(IRagRetriever ragRetriever)
    {
        _ragRetriever = ragRetriever;
    }

    public async Task<IReadOnlyList<RagDocument>> SearchAsync(
        string query,
        int topK = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RagDocument>();
        }

        var results = await _ragRetriever.SearchAsync(query, topK, cancellationToken);
        return results
            .Select(result => result.Document)
            .ToList();
    }
}
