public class ContextBuilder
{
    private const int MaxContextTokens = 4096;
    private const int ReservedOutputTokens = 1024;
    private const int MaxHistoryMessages = 6;

    private readonly TokenEstimator _tokenEstimator;

    public ContextBuilder(TokenEstimator tokenEstimator)
    {
        _tokenEstimator = tokenEstimator;
    }

    public ContextBuildResult Build(string userMessage, IReadOnlyList<LlmMessage> history)
    {
        var selectedHistory = history
            .TakeLast(MaxHistoryMessages)
            .ToList();

        var messages = new List<LlmMessage>
        {
            new("system", "You are a concise AI assistant for the AI Engineer Lab.")
        };

        messages.AddRange(selectedHistory);
        messages.Add(new("user", userMessage));

        var estimatedInputTokens = _tokenEstimator.Estimate(messages);

        return new ContextBuildResult(
            messages,
            estimatedInputTokens,
            MaxContextTokens,
            ReservedOutputTokens,
            history.Count,
            selectedHistory.Count);
    }
}
