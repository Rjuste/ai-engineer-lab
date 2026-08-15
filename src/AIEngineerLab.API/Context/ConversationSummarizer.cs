public class ConversationSummarizer
{
    public string? Summarize(IReadOnlyList<LlmMessage> history, int keepRecentMessages)
    {
        var olderMessages = history
            .Take(Math.Max(0, history.Count - keepRecentMessages))
            .ToList();

        if (olderMessages.Count == 0)
            return null;

        var dialogue = olderMessages
            .Where(message => message.Role is "user" or "assistant")
            .Select(message => $"{message.Role}: {message.Content}")
            .ToList();

        if (dialogue.Count == 0)
            return null;

        return "Summary source from older conversation: " + string.Join(" | ", dialogue);
    }
}
