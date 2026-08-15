public record LlmGenerationResult(
    string ResponseId,
    string Text,
    IReadOnlyList<LlmToolCall> ToolCalls,
    int InputTokens,
    int OutputTokens,
    int TotalTokens);
