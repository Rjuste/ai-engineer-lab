public class InMemoryRagRetriever : IRagRetriever
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;

    public InMemoryRagRetriever(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
    }

    public Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int topK = 2,
        CancellationToken cancellationToken = default)
        => SearchAsync(
            query,
            topK,
            filter: null,
            minimumSimilarity: 0,
            cancellationToken);

    public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int topK,
        RagSearchFilter? filter,
        double minimumSimilarity,
        CancellationToken cancellationToken = default)
    {
        var queryVector = await _embeddingService.EmbedAsync(query, cancellationToken);
        return _vectorStore.Search(
            queryVector,
            topK,
            filter,
            minimumSimilarity);
    }
}
