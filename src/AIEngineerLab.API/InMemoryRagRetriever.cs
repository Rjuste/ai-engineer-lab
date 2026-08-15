public class InMemoryRagRetriever : IRagRetriever
{
    private readonly List<RagDocument> _documents =
    [
        new("rag", "RAG stands for Retrieval-Augmented Generation. It retrieves relevant external information and adds it to the LLM context before generation."),
        new("embeddings", "Embeddings convert text into numeric vectors so semantically similar text can be compared using vector similarity."),
        new("chunking", "Chunking splits large documents into smaller passages that can be embedded, retrieved, and placed into an LLM context window."),
        new("evals", "AI evaluations measure system quality using repeatable test cases and metrics such as correctness, groundedness, retrieval quality, latency, and cost.")
    ];

    public IReadOnlyList<RagDocument> Retrieve(string query, int topK = 2)
    {
        var queryTerms = query
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        return _documents
            .Select(document => new
            {
                Document = document,
                Score = document.Content
                    .ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Count(queryTerms.Contains)
            })
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .Select(result => result.Document)
            .ToList();
    }
}
