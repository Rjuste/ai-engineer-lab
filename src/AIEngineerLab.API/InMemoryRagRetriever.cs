public class InMemoryRagRetriever : IRagRetriever
{
    private readonly SimpleEmbeddingService _embeddingService;

    private readonly List<RagDocument> _documents =
    [
        new("rag", "RAG stands for Retrieval-Augmented Generation. It retrieves relevant external information and adds it to the LLM context before generation."),
        new("embeddings", "Embeddings convert text into numeric vectors so semantically similar text can be compared using vector similarity."),
        new("chunking", "Chunking splits large documents into smaller passages that can be embedded, retrieved, and placed into an LLM context window."),
        new("evals", "AI evaluations measure system quality using repeatable test cases and metrics such as correctness, groundedness, retrieval quality, latency, and cost.")
    ];

    public InMemoryRagRetriever(SimpleEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public IReadOnlyList<RagSearchResult> Search(string query, int topK = 2)
    {
        var queryVector = _embeddingService.Embed(query);

        return _documents
            .Select(document =>
            {
                var documentVector = _embeddingService.Embed(document.Content);
                var score = CosineSimilarity(queryVector, documentVector);
                return new RagSearchResult(document, score);
            })
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    private static double CosineSimilarity(double[] left, double[] right)
    {
        return left.Zip(right, (a, b) => a * b).Sum();
    }
}
