public record LlmGenerationResult(
    string Text,
    int InputTokens,
    int OutputTokens,
    int TotalTokens);
