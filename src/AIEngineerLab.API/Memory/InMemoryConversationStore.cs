using System.Collections.Concurrent;

public class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<LlmMessage>> _conversations = new();

    public IReadOnlyList<LlmMessage> GetHistory(string conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var messages))
            return Array.Empty<LlmMessage>();

        lock (messages)
        {
            return messages.ToList();
        }
    }

    public void Add(string conversationId, LlmMessage message)
    {
        var messages = _conversations.GetOrAdd(conversationId, _ => new List<LlmMessage>());

        lock (messages)
        {
            messages.Add(message);
        }
    }
}
