public sealed record AgentExecutionPolicy(
    int MaxToolIterations = 4,
    int MaxToolExecutions = 6,
    int MaxTotalTokens = 12_000,
    int MaxToolRetries = 1,
    int LlmTimeoutSeconds = 20,
    int ToolTimeoutSeconds = 3);
