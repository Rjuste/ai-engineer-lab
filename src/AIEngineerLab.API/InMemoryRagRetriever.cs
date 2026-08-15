public class InMemoryRagRetriever : IRagRetriever
{
    private readonly IEmbeddingService _embeddingService;

    private readonly List<RagDocument> _documents =
    [
        new("rag", "RAG stands for Retrieval-Augmented Generation. It retrieves relevant external information and adds it to the LLM context before generation."),
        new("embeddings", "Embeddings convert text into numeric vectors so semantically similar text can be compared using vector similarity."),
        new("chunking", "Chunking splits large documents into smaller passages that can be embedded, retrieved, and placed into an LLM context window."),
        new("evals", "AI evaluations measure system quality using repeatable test cases and metrics such as correctness, groundedness, retrieval quality, latency, and cost.")
    ];

    public InMemoryRagRetriever(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int topK = 2,
        CancellationToken cancellationToken = default)
    {
        var queryVector = await _embeddingService.EmbedAsync(query, cancellationToken);
        var results = new List<RagSearchResult>();

        foreach (var document in _documents)
        {
            var documentVector = await _embeddingService.EmbedAsync(document.Content, cancellationToken);
            var score = CosineSimilarity(queryVector, documentVector);
            results.Add(new RagSearchResult(document, score));
        }

        return results
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    private static double CosineSimilarity(double[] left, double[] right)
    {
        var dotProduct = left.Zip(right, (a, b) => a * b).Sum();
        var leftMagnitude = Math.Sqrt(left.Sum(value => value * value));
        var rightMagnitude = Math.Sqrt(right.Sum(value => value * value));

        if (leftMagnitude == 0 || rightMagnitude == 0)
            return 0;

        return dotProduct / (leftMagnitude * rightMagnitude);
    }
}
