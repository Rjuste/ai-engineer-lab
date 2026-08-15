public sealed class AgentOrchestrator
{
    private readonly ILlmService _llmService;
    private readonly KnowledgeBaseSearchTool _knowledgeBaseSearchTool;

    public AgentOrchestrator(
        ILlmService llmService,
        KnowledgeBaseSearchTool knowledgeBaseSearchTool)
    {
        _llmService = llmService;
        _knowledgeBaseSearchTool = knowledgeBaseSearchTool;
    }

    public async Task<AgentRunResult> RunAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var latestUserMessage = messages
            .LastOrDefault(message => message.Role == "user")?.Content
            ?? string.Empty;

        var retrievedDocuments = await _knowledgeBaseSearchTool.SearchAsync(
            latestUserMessage,
            topK: 3,
            cancellationToken);

        var enrichedMessages = messages
            .ToList();

        if (retrievedDocuments.Count > 0)
        {
            var knowledge = string.Join(
                "\n",
                retrievedDocuments.Select(document => $"[{document.Id}] {document.Content}"));

            enrichedMessages.Add(new LlmMessage(
                "system",
                "Retrieved knowledge:\n" + knowledge));
        }

        var generation = await _llmService.GenerateAsync(enrichedMessages, cancellationToken);

        var steps = new List<AgentStep>
        {
            new(
                "knowledge_search",
                retrievedDocuments.Count > 0
                    ? $"Retrieved {retrievedDocuments.Count} relevant document(s)"
                    : "No documents matched the user's question")
        };

        return new AgentRunResult(
            generation.Text,
            steps,
            generation.InputTokens,
            generation.OutputTokens,
            generation.TotalTokens);
    }
}

public record AgentStep(string Name, string Description);

public record AgentRunResult(
    string Text,
    IReadOnlyList<AgentStep> Steps,
    int InputTokens,
    int OutputTokens,
    int TotalTokens);
