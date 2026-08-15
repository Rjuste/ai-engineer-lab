using System.Text.Json;

public sealed class AgentOrchestrator
{
    private const int MaxToolIterations = 4;

    private readonly ILlmService _llmService;
    private readonly ToolRegistry _toolRegistry;

    public AgentOrchestrator(
        ILlmService llmService,
        ToolRegistry toolRegistry)
    {
        _llmService = llmService;
        _toolRegistry = toolRegistry;
    }

    public async Task<AgentRunResult> RunAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var toolDefinitions = _toolRegistry.GetDefinitions();
        var steps = new List<AgentStep>();

        var generation = await _llmService.GenerateAsync(
            messages,
            toolDefinitions,
            cancellationToken);

        var totalInputTokens = generation.InputTokens;
        var totalOutputTokens = generation.OutputTokens;
        var totalTokens = generation.TotalTokens;

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            if (generation.ToolCalls.Count == 0)
            {
                steps.Add(new AgentStep(
                    "final_answer",
                    "The model returned a final answer without requesting another tool."));

                return new AgentRunResult(
                    generation.Text,
                    steps,
                    totalInputTokens,
                    totalOutputTokens,
                    totalTokens);
            }

            var toolOutputs = new List<LlmToolOutput>();

            foreach (var toolCall in generation.ToolCalls)
            {
                steps.Add(new AgentStep(
                    "tool_requested",
                    $"Model requested '{toolCall.Name}' with arguments {toolCall.Arguments}"));

                if (!_toolRegistry.TryGet(toolCall.Name, out var tool))
                {
                    var error = JsonSerializer.Serialize(new
                    {
                        error = $"Tool '{toolCall.Name}' is not registered or allowed."
                    });

                    steps.Add(new AgentStep(
                        "tool_rejected",
                        $"Rejected unregistered tool '{toolCall.Name}'."));

                    toolOutputs.Add(new LlmToolOutput(toolCall.CallId, error));
                    continue;
                }

                try
                {
                    var output = await tool.ExecuteAsync(
                        toolCall.Arguments,
                        cancellationToken);

                    steps.Add(new AgentStep(
                        "tool_executed",
                        $"Executed '{toolCall.Name}' successfully."));

                    toolOutputs.Add(new LlmToolOutput(toolCall.CallId, output));
                }
                catch (Exception exception)
                {
                    var error = JsonSerializer.Serialize(new
                    {
                        error = exception.Message
                    });

                    steps.Add(new AgentStep(
                        "tool_failed",
                        $"'{toolCall.Name}' failed validation or execution: {exception.Message}"));

                    toolOutputs.Add(new LlmToolOutput(toolCall.CallId, error));
                }
            }

            generation = await _llmService.ContinueWithToolOutputsAsync(
                generation.ResponseId,
                messages,
                toolOutputs,
                toolDefinitions,
                cancellationToken);

            totalInputTokens += generation.InputTokens;
            totalOutputTokens += generation.OutputTokens;
            totalTokens += generation.TotalTokens;
        }

        steps.Add(new AgentStep(
            "max_iterations_reached",
            $"Stopped after {MaxToolIterations} tool iterations."));

        return new AgentRunResult(
            string.IsNullOrWhiteSpace(generation.Text)
                ? "I could not complete the request within the allowed tool iterations."
                : generation.Text,
            steps,
            totalInputTokens,
            totalOutputTokens,
            totalTokens);
    }
}

public record AgentStep(string Name, string Description);

public record AgentRunResult(
    string Text,
    IReadOnlyList<AgentStep> Steps,
    int InputTokens,
    int OutputTokens,
    int TotalTokens);
