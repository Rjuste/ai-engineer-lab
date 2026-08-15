public class ContextBuilder
{
    public IReadOnlyList<LlmMessage> Build(string userMessage)
    {
        return new List<LlmMessage>
        {
            new("system", "You are a concise AI assistant for the AI Engineer Lab."),
            new("user", userMessage)
        };
    }
}
