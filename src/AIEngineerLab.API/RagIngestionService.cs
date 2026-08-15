public class RagIngestionService
{
    private readonly RagIngestionQueue _queue;
    private readonly RagIngestionStatusStore _statusStore;

    private readonly List<RagDocument> _seedDocuments =
    [
        new("rag", "RAG stands for Retrieval-Augmented Generation. It retrieves relevant external information and adds it to the LLM context before generation."),
        new("embeddings", "Embeddings convert text into numeric vectors so semantically similar text can be compared using vector similarity."),
        new("chunking", "Chunking splits large documents into smaller passages that can be embedded, retrieved, and placed into an LLM context window."),
        new("evals", "AI evaluations measure system quality using repeatable test cases and metrics such as correctness, groundedness, retrieval quality, latency, and cost.")
    ];

    public RagIngestionService(
        RagIngestionQueue queue,
        RagIngestionStatusStore statusStore)
    {
        _queue = queue;
        _statusStore = statusStore;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var document in _seedDocuments)
        {
            await QueueAsync(document, cancellationToken);
        }
    }

    public async Task QueueAsync(
        RagDocument document,
        CancellationToken cancellationToken = default)
    {
        _statusStore.Set(document.Id, "Queued");
        await _queue.EnqueueAsync(document, cancellationToken);
    }
}
