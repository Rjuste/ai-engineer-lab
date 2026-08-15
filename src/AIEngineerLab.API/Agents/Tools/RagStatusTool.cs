using System.Text.Json;

public sealed class RagStatusTool : IAgentTool
{
    private readonly RagIngestionStatusStore _statusStore;
    private readonly IVectorStore _vectorStore;

    public RagStatusTool(
        RagIngestionStatusStore statusStore,
        IVectorStore vectorStore)
    {
        _statusStore = statusStore;
        _vectorStore = vectorStore;
    }

    public string Name => "get_rag_status";

    public string Description =>
        "Get the current operational status of the lab's RAG index, including vector count " +
        "and ingestion states. Use this when the user asks whether documents are indexed, " +
        "whether RAG is ready, or what the current ingestion/index status is. " +
        "Do not use this to answer conceptual questions about RAG or embeddings.";

    public object Parameters => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public Task<string> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        using var arguments = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);

        if (arguments.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool arguments must be a JSON object.");

        var result = JsonSerializer.Serialize(new
        {
            vectorCount = _vectorStore.Count,
            documents = _statusStore.GetAll()
        });

        return Task.FromResult(result);
    }
}
