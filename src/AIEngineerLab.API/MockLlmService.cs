public class MockLlmService : ILlmService
{
    public Task<string> GenerateAsync(string message)
    {
        return Task.FromResult($"Mock LLM received: {message}");
    }
}
