public record ContextBuildResult(
    IReadOnlyList<LlmMessage> Messages,
    int EstimatedInputTokens,
    int MaxContextTokens,
    int ReservedOutputTokens)
{
    public int MaxInputTokens => MaxContextTokens - ReservedOutputTokens;
    public int RemainingInputTokens => MaxInputTokens - EstimatedInputTokens;
}
