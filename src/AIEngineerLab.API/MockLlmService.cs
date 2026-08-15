public class MockLlmService : ILlmService
{
    public Task<string> GenerateAsync(IReadOnlyList<LlmMessage> context)
    {
        var userMessage = context.Last(x => x.Role == "user").Content;
        return Task.FromResult($"Mock LLM received {context.Count} message(s). User said: {userMessage}");
    }
}
