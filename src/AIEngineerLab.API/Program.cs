var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmService, MockLlmService>();
builder.Services.AddSingleton<TokenEstimator>();
builder.Services.AddSingleton<ConversationSummarizer>();
builder.Services.AddSingleton<ContextBuilder>();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();

var app = builder.Build();

app.MapGet("/", () =>
{
    return "AI Engineer Lab is running!";
});

app.MapPost("/api/chat/{conversationId}", async (
    string conversationId,
    ChatRequest request,
    IConversationStore conversationStore,
    ContextBuilder contextBuilder,
    ILlmService llm) =>
{
    var history = conversationStore.GetHistory(conversationId);
    var context = contextBuilder.Build(request.Message, history);
    var answer = await llm.GenerateAsync(context.Messages);

    conversationStore.Add(conversationId, new LlmMessage("user", request.Message));
    conversationStore.Add(conversationId, new LlmMessage("assistant", answer));

    return Results.Ok(new
    {
        conversationId,
        answer,
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
