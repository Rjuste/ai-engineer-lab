var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmService, MockLlmService>();
builder.Services.AddSingleton<TokenEstimator>();
builder.Services.AddSingleton<ContextBuilder>();

var app = builder.Build();

app.MapGet("/", () =>
{
    return "AI Engineer Lab is running!";
});

app.MapPost("/api/chat", async (ChatRequest request, ContextBuilder contextBuilder, ILlmService llm) =>
{
    var context = contextBuilder.Build(request.Message);
    var answer = await llm.GenerateAsync(context.Messages);

    return Results.Ok(new
    {
        answer,
        context = context.Messages,
        tokenBudget = new
        {
            context.EstimatedInputTokens,
            context.MaxContextTokens,
            context.ReservedOutputTokens,
            context.MaxInputTokens,
            context.RemainingInputTokens
        }
    });
});

app.Run();
