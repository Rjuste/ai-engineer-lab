var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILlmService, MockLlmService>();

var app = builder.Build();

app.MapGet("/", () =>
{
    return "AI Engineer Lab is running!";
});

app.MapPost("/api/chat", async (ChatRequest request, ILlmService llm) =>
{
    var answer = await llm.GenerateAsync(request.Message);

    return Results.Ok(new
    {
        answer
    });
});

app.Run();
