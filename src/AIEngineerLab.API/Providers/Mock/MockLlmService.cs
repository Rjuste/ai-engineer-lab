public class MockLlmService : ILlmService
{
    public Task<LlmGenerationResult> GenerateAsync(
        IReadOnlyList<LlmMessage> context,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        var userMessage = context.Last(x => x.Role == "user").Content;
        var text = $"Mock LLM received {context.Count} message(s). User said: {userMessage}";

        return Task.FromResult(new LlmGenerationResult(
            "mock-response",
            text,
            Array.Empty<LlmToolCall>(),
            0,
            0,
            0));
    }

    public Task<LlmGenerationResult> ContinueWithToolOutputsAsync(
        string previousResponseId,
        IReadOnlyList<LlmMessage> context,
        IReadOnlyList<LlmToolOutput> toolOutputs,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LlmGenerationResult(
            "mock-response-continued",
            "Mock LLM received tool output.",
            Array.Empty<LlmToolCall>(),
            0,
            0,
            0));
    }
}
