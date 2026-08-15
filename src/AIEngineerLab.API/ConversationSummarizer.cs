public class ConversationSummarizer
{
    public string? Summarize(IReadOnlyList<LlmMessage> history, int keepRecentMessages)
    {
        var olderMessages = history
            .Take(Math.Max(0, history.Count - keepRecentMessages))
            .ToList();

        if (olderMessages.Count == 0)
            return null;

        var facts = olderMessages
            .Where(message => message.Role == "user")
            .Select(message => message.Content)
            .ToList();

        if (facts.Count == 0)
            return null;

        return "Summary of older conversation: " + string.Join(" | ", facts);
    }
}
