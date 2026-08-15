var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmService, MockLlmService>();
builder.Services.AddSingleton<TokenEstimator>();
builder.Services.AddSingleton<ConversationSummarizer>();
builder.Services.AddSingleton<ContextBuilder>();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSingleton<IRagRetriever, InMemoryRagRetriever>();

var app = builder.Build();

app.MapGet("/", () =>
{
    return "AI Engineer Lab is running!";
});

app.MapPost("/api/chat/{conversationId}", async (
    string conversationId,
    ChatRequest request,
    IConversationStore conversationStore,
    IRagRetriever ragRetriever,
    ContextBuilder contextBuilder,
    ILlmService llm) =>
{
    var history = conversationStore.GetHistory(conversationId);
    var retrievedDocuments = ragRetriever.Retrieve(request.Message);
    var context = contextBuilder.Build(request.Message, history, retrievedDocuments);
    var answer = await llm.GenerateAsync(context.Messages);

    conversationStore.Add(conversationId, new LlmMessage("user", request.Message));
    conversationStore.Add(conversationId, new LlmMessage("assistant", answer));

    return Results.Ok(new
    {
        conversationId,
        answer,
        retrievedDocuments,
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
