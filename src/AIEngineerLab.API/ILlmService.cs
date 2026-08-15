public interface ILlmService
{
    Task<LlmGenerationResult> GenerateAsync(
        IReadOnlyList<LlmMessage> context,
        CancellationToken cancellationToken = default);
}
