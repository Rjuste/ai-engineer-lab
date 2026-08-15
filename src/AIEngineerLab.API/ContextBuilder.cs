public class ContextBuilder
{
    private const int MaxContextTokens = 4096;
    private const int ReservedOutputTokens = 1024;

    private readonly TokenEstimator _tokenEstimator;

    public ContextBuilder(TokenEstimator tokenEstimator)
    {
        _tokenEstimator = tokenEstimator;
    }

    public ContextBuildResult Build(string userMessage)
    {
        var messages = new List<LlmMessage>
        {
            new("system", "You are a concise AI assistant for the AI Engineer Lab."),
            new("user", userMessage)
        };

        var estimatedInputTokens = _tokenEstimator.Estimate(messages);

        return new ContextBuildResult(
            messages,
            estimatedInputTokens,
            MaxContextTokens,
            ReservedOutputTokens);
    }
}
