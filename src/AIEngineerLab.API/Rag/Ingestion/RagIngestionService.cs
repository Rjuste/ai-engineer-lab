public class RagIngestionService
{
    private readonly RagIngestionQueue _queue;
    private readonly RagIngestionStatusStore _statusStore;

    private readonly List<RagDocument> _documents =
    [
        new("rag", "RAG stands for Retrieval-Augmented Generation. It retrieves relevant external information and adds it to the LLM context before generation.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),
        new("embeddings", "Embeddings convert text into numeric vectors so semantically similar text can be compared using vector similarity.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),
        new("chunking", "Chunking splits large documents into smaller passages that can be embedded, retrieved, and placed into an LLM context window.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),
        new("evals", "AI evaluations measure system quality using repeatable test cases and metrics such as correctness, groundedness, retrieval quality, latency, and cost.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),

        new("pto-us-2026", "US employees receive 15 days of paid time off annually. Unused PTO may carry over into the following calendar year up to a maximum accumulated balance of 25 days.", new RagMetadata("tenant-123", "US", 2026, "HR")),
        new("pto-uk-2026", "UK employees receive 25 days of annual leave. Unused annual leave may carry over subject to the UK employee handbook rules.", new RagMetadata("tenant-123", "UK", 2026, "HR")),
        new("pto-us-2024", "In 2024 US employees received 20 days of paid time off annually under the previous policy.", new RagMetadata("tenant-123", "US", 2024, "HR")),
        new("pto-other-tenant", "Employees receive 18 days of paid time off annually under tenant 999 policy.", new RagMetadata("tenant-999", "US", 2026, "HR")),

        new("incident-err-x92", "Incident INC-84721: ERR_X92 occurred because the payment processor timed out after Stripe webhook delivery exceeded the configured timeout.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),
        new("payment-timeout-guide", "Payment processing can fail when a webhook or downstream payment processor request exceeds its timeout. The system records the failure and the invoice remains unpaid until retry or resolution.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),
        new("error-reference", "ERR_X92 is a payment-processing error code used by the invoice workflow.", new RagMetadata("tenant-123", "US", 2026, "Engineering")),
        new("invoice-processing", "Invoices are processed asynchronously after creation. Payment state is updated after the processor response is received.", new RagMetadata("tenant-123", "US", 2026, "Engineering"))
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

        if (existingStatus is not null)
            return false;

        _statusStore.Set(job.IdempotencyKey, "Queued");
        await _queue.EnqueueAsync(job, cancellationToken);
        return true;
    }
}
