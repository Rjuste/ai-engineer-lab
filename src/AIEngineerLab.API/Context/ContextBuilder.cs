public class ContextBuilder
{
    private const int MaxContextTokens = 4096;
    private const int ReservedOutputTokens = 1024;
    private const int MaxHistoryMessages = 6;

    private readonly TokenEstimator _tokenEstimator;
    private readonly ConversationSummarizer _conversationSummarizer;

    public ContextBuilder(
        TokenEstimator tokenEstimator,
        ConversationSummarizer conversationSummarizer)
    {
        _tokenEstimator = tokenEstimator;
        _conversationSummarizer = conversationSummarizer;
    }

    public ContextBuildResult Build(
        string userMessage,
        IReadOnlyList<LlmMessage> history,
        IReadOnlyList<RagDocument> retrievedDocuments)
    {
        var selectedHistory = history
            .TakeLast(MaxHistoryMessages)
            .ToList();

        var summary = _conversationSummarizer.Summarize(history, MaxHistoryMessages);

        var messages = new List<LlmMessage>
        {
            new("system", "You are a concise AI assistant for the AI Engineer Lab. Use retrieved knowledge when it is relevant to the user's question.")
        };

        if (summary is not null)
        {
            messages.Add(new LlmMessage("system", summary));
        }

        if (retrievedDocuments.Count > 0)
        {
            var retrievedKnowledge = string.Join(
                "\n",
                retrievedDocuments.Select(document => $"[{document.Id}] {document.Content}"));

            messages.Add(new LlmMessage(
                "system",
                "Retrieved knowledge:\n" + retrievedKnowledge));
        }

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
