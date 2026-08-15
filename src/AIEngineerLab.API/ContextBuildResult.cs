public record ContextBuildResult(
    IReadOnlyList<LlmMessage> Messages,
    int EstimatedInputTokens,
    int MaxContextTokens,
    int ReservedOutputTokens,
    int TotalHistoryMessages,
    int IncludedHistoryMessages)
{
    public int MaxInputTokens => MaxContextTokens - ReservedOutputTokens;
    public int RemainingInputTokens => MaxInputTokens - EstimatedInputTokens;
    public int DroppedHistoryMessages => TotalHistoryMessages - IncludedHistoryMessages;
}
