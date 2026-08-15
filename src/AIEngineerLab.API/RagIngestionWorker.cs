public class RagIngestionWorker : BackgroundService
{
    private const int MaxAttempts = 3;

    private readonly RagIngestionQueue _queue;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly DocumentChunker _chunker;
    private readonly RagIngestionStatusStore _statusStore;
    private readonly DeadLetterStore _deadLetterStore;
    private readonly ILogger<RagIngestionWorker> _logger;

    public RagIngestionWorker(
        RagIngestionQueue queue,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        DocumentChunker chunker,
        RagIngestionStatusStore statusStore,
        DeadLetterStore deadLetterStore,
        ILogger<RagIngestionWorker> logger)
    {
        _queue = queue;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _chunker = chunker;
        _statusStore = statusStore;
        _deadLetterStore = deadLetterStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(job, stoppingToken);
        }
    }

    private async Task ProcessAsync(
        RagIngestionJob job,
        CancellationToken cancellationToken)
    {
        var key = job.IdempotencyKey;

        if (_statusStore.Get(key) == "Indexed")
            return;

        if (!_statusStore.TryClaim(key))
            return;

        try
        {
            _statusStore.Set(key, $"Processing (attempt {job.Attempt})");

            // Deliberate failure hook for learning retry/dead-letter behavior.
            if (job.Document.Content.Contains("FAIL_INGESTION", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Simulated ingestion failure.");

            foreach (var chunk in _chunker.Chunk(job.Document))
            {
                var embedding = await _embeddingService.EmbedAsync(
                    chunk.Content,
                    cancellationToken);

                _vectorStore.Add(chunk, embedding);
            }

            _statusStore.Set(key, "Indexed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (job.Attempt < MaxAttempts)
            {
                _statusStore.Set(key, $"Retrying ({job.Attempt}/{MaxAttempts})");
                _statusStore.ReleaseClaim(key);

                await Task.Delay(TimeSpan.FromSeconds(job.Attempt), cancellationToken);
                await _queue.EnqueueAsync(job with { Attempt = job.Attempt + 1 }, cancellationToken);
                return;
            }

            _statusStore.Set(key, "DeadLettered");
            _deadLetterStore.Add(new DeadLetterEntry(
                key,
                job.Document.Id,
                job.Version,
                job.Attempt,
                exception.Message));

            _logger.LogError(
                exception,
                "Dead-lettered RAG ingestion job {IdempotencyKey}",
                key);
        }
        finally
        {
            if (_statusStore.Get(key) != "Retrying")
                _statusStore.ReleaseClaim(key);
        }
    }
}
