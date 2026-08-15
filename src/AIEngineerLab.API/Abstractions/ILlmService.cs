public interface ILlmService
{
    Task<LlmGenerationResult> GenerateAsync(
        IReadOnlyList<LlmMessage> context,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default);

    Task<LlmGenerationResult> ContinueWithToolOutputsAsync(
        string previousResponseId,
        IReadOnlyList<LlmMessage> context,
        IReadOnlyList<LlmToolOutput> toolOutputs,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default);
}
