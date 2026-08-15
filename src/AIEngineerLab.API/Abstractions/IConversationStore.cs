public interface IConversationStore
{
    IReadOnlyList<LlmMessage> GetHistory(string conversationId);
    void Add(string conversationId, LlmMessage message);
}
