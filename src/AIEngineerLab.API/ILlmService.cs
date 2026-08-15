public interface ILlmService
{
    Task<string> GenerateAsync(IReadOnlyList<LlmMessage> context);
}
