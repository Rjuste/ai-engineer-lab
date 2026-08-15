var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmService, MockLlmService>();
builder.Services.AddSingleton<TokenEstimator>();
builder.Services.AddSingleton<ConversationSummarizer>();
builder.Services.AddSingleton<ContextBuilder>();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddSingleton<DocumentChunker>();
builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();
builder.Services.AddSingleton<RagIngestionQueue>();
builder.Services.AddSingleton<RagIngestionStatusStore>();
builder.Services.AddSingleton<DeadLetterStore>();
builder.Services.AddSingleton<RagIngestionService>();
builder.Services.AddHostedService<RagIngestionWorker>();
builder.Services.AddSingleton<IRagRetriever, InMemoryRagRetriever>();

var app = builder.Build();

var ingestionService = app.Services.GetRequiredService<RagIngestionService>();
await ingestionService.SeedAsync();

app.MapGet("/", () => "AI Engineer Lab is running!");

app.MapGet("/api/rag/status", (
    RagIngestionStatusStore statusStore,
    IVectorStore vectorStore) => Results.Ok(new
{
    vectorCount = vectorStore.Count,
    documents = statusStore.GetAll()
}));

app.MapGet("/api/rag/deadletters", (
    DeadLetterStore deadLetters) => Results.Ok(deadLetters.GetAll()));

app.MapPost("/api/rag/documents", async (
    RagDocument document,
    int? version,
    RagIngestionService ingestion,
    CancellationToken cancellationToken) =>
{
    var documentVersion = version ?? 1;
    await ingestion.QueueAsync(document, documentVersion, cancellationToken);

    return Results.Accepted(value: new
    {
        document.Id,
        version = documentVersion,
        idempotencyKey = $"{document.Id}:{documentVersion}",
        status = "Queued"
    });
});

app.MapPost("/api/chat/{conversationId}", async (
    string conversationId,
    ChatRequest request,
    IConversationStore conversationStore,
    IRagRetriever ragRetriever,
    ContextBuilder contextBuilder,
    ILlmService llm,
    CancellationToken cancellationToken) =>
{
    var history = conversationStore.GetHistory(conversationId);
    var retrievalResults = await ragRetriever.SearchAsync(
        request.Message,
        cancellationToken: cancellationToken);
    var retrievedDocuments = retrievalResults.Select(result => result.Document).ToList();
    var context = contextBuilder.Build(request.Message, history, retrievedDocuments);
    var answer = await llm.GenerateAsync(context.Messages);

    conversationStore.Add(conversationId, new LlmMessage("user", request.Message));
    conversationStore.Add(conversationId, new LlmMessage("assistant", answer));

    return Results.Ok(new
    {
        conversationId,
        answer,
        retrievalResults,
        context = context.Messages,
        tokenBudget = new
        {
            context.EstimatedInputTokens,
            context.MaxContextTokens,
            context.ReservedOutputTokens,
            context.MaxInputTokens,
            context.RemainingInputTokens,
            context.TotalHistoryMessages,
            context.IncludedHistoryMessages,
            context.DroppedHistoryMessages
        }
    });
});

app.Run();
