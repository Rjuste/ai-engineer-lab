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
builder.Services.AddSingleton<AdvancedRagPipeline>();
builder.Services.AddSingleton<RetrievalEvalHarness>();

builder.Services.AddSingleton<IAgentTool, KnowledgeBaseSearchTool>();
builder.Services.AddSingleton<IAgentTool, RagStatusTool>();
builder.Services.AddSingleton<IAgentTool, DivisionTool>();
builder.Services.AddSingleton<IAgentTool, ResilienceSimulationTool>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton(new AgentExecutionPolicy());
builder.Services.AddSingleton<AgentOrchestrator>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
var ingestionService = app.Services.GetRequiredService<RagIngestionService>();
await ingestionService.SeedAsync();
app.MapGet("/", () => Results.Redirect("/lab/"));
app.MapGet("/api/agent/policy", (AgentExecutionPolicy policy) => Results.Ok(policy));
app.MapGet("/api/rag/status", (RagIngestionStatusStore statusStore, IVectorStore vectorStore) => Results.Ok(new { vectorCount = vectorStore.Count, documents = statusStore.GetAll() }));
app.MapGet("/api/rag/deadletters", (DeadLetterStore deadLetters) => Results.Ok(deadLetters.GetAll()));

app.MapPost("/api/rag/search", async (RagSearchRequest request, IRagRetriever retriever, IVectorStore vectorStore, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Query)) return Results.BadRequest(new { error = "Query is required." });
    var filter = new RagSearchFilter(request.TenantId, request.Country, request.Year, request.Department);
    var topK = Math.Clamp(request.TopK, 1, 50);
    var minimumSimilarity = Math.Clamp(request.MinimumSimilarity, -1, 1);
    var results = await retriever.SearchAsync(request.Query, topK, filter, minimumSimilarity, cancellationToken);
    return Results.Ok(new { query = request.Query, totalVectorCount = vectorStore.Count, eligibleVectorCount = vectorStore.CountEligible(filter), returnedCount = results.Count, filter, minimumSimilarity, topK, note = "Metadata filtering is applied before similarity scoring. Tenant/user authorization must come from trusted backend identity in production.", results = results.Select(result => new { documentId = result.Document.Id, content = result.Document.Content, metadata = result.Document.Metadata, similarity = result.Score }) });
});

app.MapPost("/api/rag/advanced-search", async (AdvancedRagSearchRequest request, AdvancedRagPipeline pipeline, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Query)) return Results.BadRequest(new { error = "Query is required." });
    try
    {
        var result = await pipeline.SearchAsync(request, cancellationToken);
        return Results.Ok(new
        {
            result.OriginalQuery, result.SearchQuery, result.QueryWasRewritten,
            rewriteUsage = new { inputTokens = result.RewriteInputTokens, outputTokens = result.RewriteOutputTokens, totalTokens = result.RewriteInputTokens + result.RewriteOutputTokens },
            result.Filter, result.TotalVectorCount, result.EligibleVectorCount,
            settings = new { result.CandidateTopK, result.FinalTopK, result.MinimumVectorSimilarity, result.MinimumRerankScore },
            stages = new
            {
                vector = result.VectorResults.Select((item, index) => new { rank = index + 1, documentId = item.Document.Id, similarity = item.Score, metadata = item.Document.Metadata, content = item.Document.Content }),
                keyword = result.KeywordResults.Select((item, index) => new { rank = index + 1, documentId = item.Document.Id, keywordScore = item.Score, metadata = item.Document.Metadata, content = item.Document.Content }),
                fusedAndReranked = result.FusedCandidates.Select((item, index) => new { rank = index + 1, documentId = item.Document.Id, item.VectorRank, item.VectorScore, item.KeywordRank, item.KeywordScore, item.RrfScore, item.RerankScore, metadata = item.Document.Metadata, content = item.Document.Content }),
                finalContext = result.FinalResults.Select((item, index) => new { rank = index + 1, documentId = item.Document.Id, item.RerankScore, item.RrfScore, metadata = item.Document.Metadata, content = item.Document.Content })
            },
            notes = new[] { "Metadata filters run before retrieval; authorization scope must be backend-controlled in production.", "Vector and keyword raw scores are not compared directly; RRF combines their ranks.", "The lab reranker is deterministic and observable. Replace it with a dedicated reranking/cross-encoder model for production quality.", "Only candidates above MinimumRerankScore and within FinalTopK enter final context." }
        });
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapGet("/api/evals/retrieval/cases", (RetrievalEvalHarness harness) => Results.Ok(harness.GoldenDataset));
app.MapPost("/api/evals/retrieval/run", async (int? k, int? runs, RetrievalEvalHarness harness, CancellationToken cancellationToken) =>
{
    var report = await harness.RunAsync(k ?? 5, runs ?? 1, cancellationToken);
    return Results.Ok(report);
});

app.MapPost("/api/rag/documents", async (RagDocument document, int? version, RagIngestionService ingestion, RagIngestionStatusStore statusStore, CancellationToken cancellationToken) =>
{
    var documentVersion = version ?? 1;
    var key = $"{document.Id}:{documentVersion}";
    var queued = await ingestion.QueueAsync(document, documentVersion, cancellationToken);
    if (!queued) return Results.Ok(new { document.Id, version = documentVersion, idempotencyKey = key, status = statusStore.Get(key), duplicate = true });
    return Results.Accepted(value: new { document.Id, version = documentVersion, idempotencyKey = key, status = "Queued", duplicate = false });
});

app.MapPost("/api/chat/{conversationId}", async (string conversationId, ChatRequest request, IConversationStore conversationStore, ContextBuilder contextBuilder, AgentOrchestrator agent, AgentExecutionPolicy policy, CancellationToken cancellationToken) =>
{
    var history = conversationStore.GetHistory(conversationId);
    var context = contextBuilder.Build(request.Message, history, Array.Empty<RagDocument>());
    var result = await agent.RunAsync(context.Messages, cancellationToken);
    conversationStore.Add(conversationId, new LlmMessage("user", request.Message));
    conversationStore.Add(conversationId, new LlmMessage("assistant", result.Text));
    return Results.Ok(new { conversationId, answer = result.Text, agentSteps = result.Steps, trace = result.Trace, llmUsage = new { result.InputTokens, result.OutputTokens, result.TotalTokens }, executionPolicy = policy, context = context.Messages });
});

app.Run();
