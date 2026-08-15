public class RagIngestionWorker : BackgroundService
{
    private readonly RagIngestionQueue _queue;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly DocumentChunker _chunker;
    private readonly RagIngestionStatusStore _statusStore;
    private readonly ILogger<RagIngestionWorker> _logger;

    public RagIngestionWorker(
        RagIngestionQueue queue,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        DocumentChunker chunker,
        RagIngestionStatusStore statusStore,
        ILogger<RagIngestionWorker> logger)
    {
        _queue = queue;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _chunker = chunker;
        _statusStore = statusStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var document in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                _statusStore.Set(document.Id, "Processing");

                foreach (var chunk in _chunker.Chunk(document))
                {
                    var embedding = await _embeddingService.EmbedAsync(
                        chunk.Content,
                        stoppingToken);

                    _vectorStore.Add(chunk, embedding);
                }

                _statusStore.Set(document.Id, "Indexed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _statusStore.Set(document.Id, "Failed");
                _logger.LogError(
                    exception,
                    "Failed to ingest RAG document {DocumentId}",
                    document.Id);
            }
        }
    }
}
