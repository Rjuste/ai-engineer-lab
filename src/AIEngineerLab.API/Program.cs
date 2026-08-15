var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<ILlmService, OpenAiLlmService>();
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

builder.Services.AddSingleton<IAgentTool, KnowledgeBaseSearchTool>();
builder.Services.AddSingleton<IAgentTool, RagStatusTool>();
builder.Services.AddSingleton<IAgentTool, DivisionTool>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<AgentOrchestrator>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var ingestionService = app.Services.GetRequiredService<RagIngestionService>();
await ingestionService.SeedAsync();

app.MapGet("/", () => Results.Redirect("/lab/"));

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
    RagIngestionStatusStore statusStore,
    CancellationToken cancellationToken) =>
{
    var documentVersion = version ?? 1;
    var key = $"{document.Id}:{documentVersion}";
    var queued = await ingestion.QueueAsync(
        document,
        documentVersion,
        cancellationToken);

    if (!queued)
    {
        return Results.Ok(new
        {
            document.Id,
            version = documentVersion,
            idempotencyKey = key,
            status = statusStore.Get(key),
            duplicate = true
        });
    }

    return Results.Accepted(value: new
    {
        document.Id,
        version = documentVersion,
        idempotencyKey = key,
        status = "Queued",
        duplicate = false
    });
});

app.MapPost("/api/chat/{conversationId}", async (
    string conversationId,
    ChatRequest request,
    IConversationStore conversationStore,
    ContextBuilder contextBuilder,
    AgentOrchestrator agent,
    CancellationToken cancellationToken) =>
{
    var history = conversationStore.GetHistory(conversationId);
    var context = contextBuilder.Build(
        request.Message,
        history,
        Array.Empty<RagDocument>());

    var result = await agent.RunAsync(
        context.Messages,
        cancellationToken);

    conversationStore.Add(
        conversationId,
        new LlmMessage("user", request.Message));

    conversationStore.Add(
        conversationId,
        new LlmMessage("assistant", result.Text));

    return Results.Ok(new
    {
        conversationId,
        answer = result.Text,
        agentSteps = result.Steps,
        llmUsage = new
        {
            result.InputTokens,
            result.OutputTokens,
            result.TotalTokens
        },
        context = context.Messages
    });
});

app.Run();
