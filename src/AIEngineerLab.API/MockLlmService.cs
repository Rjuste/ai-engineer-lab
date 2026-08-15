public class MockLlmService : ILlmService
{
    public Task<LlmGenerationResult> GenerateAsync(
        IReadOnlyList<LlmMessage> context,
        CancellationToken cancellationToken = default)
    {
        var userMessage = context.Last(x => x.Role == "user").Content;
        var text = $"Mock LLM received {context.Count} message(s). User said: {userMessage}";

        return Task.FromResult(new LlmGenerationResult(text, 0, 0, 0));
    }
}
