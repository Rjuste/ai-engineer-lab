public class RagIngestionService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly DocumentChunker _chunker;

    private readonly List<RagDocument> _documents =
    [
        new("rag", "RAG stands for Retrieval-Augmented Generation. It retrieves relevant external information and adds it to the LLM context before generation."),
        new("embeddings", "Embeddings convert text into numeric vectors so semantically similar text can be compared using vector similarity."),
        new("chunking", "Chunking splits large documents into smaller passages that can be embedded, retrieved, and placed into an LLM context window."),
        new("evals", "AI evaluations measure system quality using repeatable test cases and metrics such as correctness, groundedness, retrieval quality, latency, and cost.")
    ];

    public RagIngestionService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        DocumentChunker chunker)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _chunker = chunker;
    }

    public async Task IngestAsync(CancellationToken cancellationToken = default)
    {
        if (_vectorStore.Count > 0)
            return;

        foreach (var document in _documents)
        {
            foreach (var chunk in _chunker.Chunk(document))
            {
                var embedding = await _embeddingService.EmbedAsync(chunk.Content, cancellationToken);
                _vectorStore.Add(chunk, embedding);
            }
        }
    }
}
