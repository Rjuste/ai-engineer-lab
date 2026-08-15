public class RagIngestionService
{
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Indexed",
            "DeadLettered"
        };

    private readonly RagIngestionQueue _queue;
    private readonly RagIngestionStatusStore _statusStore;

    private readonly List<RagDocument> _documents =
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
        foreach (var document in _documents)
            await QueueAsync(document, 1, cancellationToken);
    }

    public async Task<bool> QueueAsync(
        RagDocument document,
        int version = 1,
        CancellationToken cancellationToken = default)
    {
        var job = new RagIngestionJob(document, version);
        var existingStatus = _statusStore.Get(job.IdempotencyKey);

        // Any existing lifecycle record means this logical operation has already
        // been accepted. Terminal failures require an explicit redrive operation.
        if (existingStatus is not null)
            return false;

        _statusStore.Set(job.IdempotencyKey, "Queued");
        await _queue.EnqueueAsync(job, cancellationToken);
        return true;
    }
}
