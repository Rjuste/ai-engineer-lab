using System.Diagnostics;
using System.Text.Json;

public sealed class AgentOrchestrator
{
    private readonly ILlmService _llmService;
    private readonly ToolRegistry _toolRegistry;
    private readonly AgentExecutionPolicy _policy;

    public AgentOrchestrator(
        ILlmService llmService,
        ToolRegistry toolRegistry,
        AgentExecutionPolicy policy)
    {
        _llmService = llmService;
        _toolRegistry = toolRegistry;
        _policy = policy;
    }

    public async Task<AgentRunResult> RunAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var toolDefinitions = _toolRegistry.GetDefinitions();
        var steps = new List<AgentStep>();
        var toolExecutions = 0;

        LlmGenerationResult generation;

        try
        {
            generation = await RunWithTimeoutAsync(
                token => _llmService.GenerateAsync(messages, toolDefinitions, token),
                _policy.LlmTimeoutSeconds,
                cancellationToken);
        }
        catch (TimeoutException exception)
        {
            steps.Add(new AgentStep("llm_timeout", exception.Message));
            return new AgentRunResult(
                "The model did not respond within the allowed time.",
                steps,
                0,
                0,
                0);
        }

        var totalInputTokens = generation.InputTokens;
        var totalOutputTokens = generation.OutputTokens;
        var totalTokens = generation.TotalTokens;

        for (var iteration = 0; iteration < _policy.MaxToolIterations; iteration++)
        {
            if (totalTokens > _policy.MaxTotalTokens)
            {
                steps.Add(new AgentStep(
                    "token_budget_exceeded",
                    $"Stopped because total token usage {totalTokens} exceeded the budget of {_policy.MaxTotalTokens}."));

                return BuildStoppedResult(
                    generation,
                    steps,
                    totalInputTokens,
                    totalOutputTokens,
                    totalTokens,
                    "I stopped because the agent reached its token budget.");
            }

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
                if (toolExecutions >= _policy.MaxToolExecutions)
                {
                    steps.Add(new AgentStep(
                        "tool_budget_exceeded",
                        $"Stopped after {_policy.MaxToolExecutions} tool executions."));

                    return BuildStoppedResult(
                        generation,
                        steps,
                        totalInputTokens,
                        totalOutputTokens,
                        totalTokens,
                        "I stopped because the agent reached its tool execution budget.");
                }

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

                toolExecutions++;
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var (output, attempts) = await ExecuteToolWithPolicyAsync(
                        tool,
                        toolCall.Arguments,
                        cancellationToken);

                    stopwatch.Stop();

                    steps.Add(new AgentStep(
                        "tool_executed",
                        $"Executed '{toolCall.Name}' successfully in {stopwatch.ElapsedMilliseconds} ms after {attempts} attempt(s)."));

                    toolOutputs.Add(new LlmToolOutput(toolCall.CallId, output));
                }
                catch (ArgumentException exception)
                {
                    stopwatch.Stop();
                    AddToolFailure(toolOutputs, steps, toolCall, exception, "tool_validation_failed");
                }
                catch (JsonException exception)
                {
                    stopwatch.Stop();
                    AddToolFailure(toolOutputs, steps, toolCall, exception, "tool_validation_failed");
                }
                catch (TimeoutException exception)
                {
                    stopwatch.Stop();
                    AddToolFailure(toolOutputs, steps, toolCall, exception, "tool_timeout");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    AddToolFailure(toolOutputs, steps, toolCall, exception, "tool_failed");
                }
            }

            try
            {
                generation = await RunWithTimeoutAsync(
                    token => _llmService.ContinueWithToolOutputsAsync(
                        generation.ResponseId,
                        messages,
                        toolOutputs,
                        toolDefinitions,
                        token),
                    _policy.LlmTimeoutSeconds,
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                steps.Add(new AgentStep("llm_timeout", exception.Message));

                return BuildStoppedResult(
                    generation,
                    steps,
                    totalInputTokens,
                    totalOutputTokens,
                    totalTokens,
                    "The model timed out while continuing after a tool call.");
            }

            totalInputTokens += generation.InputTokens;
            totalOutputTokens += generation.OutputTokens;
            totalTokens += generation.TotalTokens;
        }

        steps.Add(new AgentStep(
            "max_iterations_reached",
            $"Stopped after {_policy.MaxToolIterations} model/tool iterations."));

        return BuildStoppedResult(
            generation,
            steps,
            totalInputTokens,
            totalOutputTokens,
            totalTokens,
            "I could not complete the request within the allowed agent iterations.");
    }

    private async Task<(string Output, int Attempts)> ExecuteToolWithPolicyAsync(
        IAgentTool tool,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var maxAttempts = _policy.MaxToolRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var output = await RunWithTimeoutAsync(
                    token => tool.ExecuteAsync(argumentsJson, token),
                    _policy.ToolTimeoutSeconds,
                    cancellationToken);

                return (output, attempt);
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("Tool execution failed after all retry attempts.");
    }

    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation exceeded the {timeoutSeconds}-second timeout.");
        }
    }

    private static void AddToolFailure(
        List<LlmToolOutput> toolOutputs,
        List<AgentStep> steps,
        LlmToolCall toolCall,
        Exception exception,
        string stepName)
    {
        var error = JsonSerializer.Serialize(new
        {
            error = exception.Message
        });

        steps.Add(new AgentStep(
            stepName,
            $"'{toolCall.Name}' did not execute: {exception.Message}"));

        toolOutputs.Add(new LlmToolOutput(toolCall.CallId, error));
    }

    private static AgentRunResult BuildStoppedResult(
        LlmGenerationResult generation,
        IReadOnlyList<AgentStep> steps,
        int inputTokens,
        int outputTokens,
        int totalTokens,
        string fallbackText)
    {
        return new AgentRunResult(
            string.IsNullOrWhiteSpace(generation.Text)
                ? fallbackText
                : generation.Text,
            steps,
            inputTokens,
            outputTokens,
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
