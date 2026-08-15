using System.Diagnostics;
using System.Text.Json;

public sealed class AgentOrchestrator
{
    private readonly ILlmService _llmService;
    private readonly ToolRegistry _toolRegistry;
    private readonly AgentExecutionPolicy _policy;

    public AgentOrchestrator(ILlmService llmService, ToolRegistry toolRegistry, AgentExecutionPolicy policy)
    {
        _llmService = llmService;
        _toolRegistry = toolRegistry;
        _policy = policy;
    }

    public async Task<AgentRunResult> RunAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var runStopwatch = Stopwatch.StartNew();
        var toolDefinitions = _toolRegistry.GetDefinitions();
        var steps = new List<AgentStep>();
        var spans = new List<AgentTraceSpan>();
        var toolExecutions = 0;
        var llmCalls = 0;

        LlmGenerationResult generation;
        try
        {
            generation = await CallLlmAsync(
                "initial_generation",
                token => _llmService.GenerateAsync(messages, toolDefinitions, token),
                spans,
                ++llmCalls,
                cancellationToken);
        }
        catch (TimeoutException exception)
        {
            steps.Add(new AgentStep("llm_timeout", exception.Message));
            return Finish("The model did not respond within the allowed time.", "timeout", 0, 0, 0);
        }

        var totalInputTokens = generation.InputTokens;
        var totalOutputTokens = generation.OutputTokens;
        var totalTokens = generation.TotalTokens;

        for (var iteration = 0; iteration < _policy.MaxToolIterations; iteration++)
        {
            if (totalTokens > _policy.MaxTotalTokens)
            {
                steps.Add(new AgentStep("token_budget_exceeded", $"Stopped because total token usage {totalTokens} exceeded the budget of {_policy.MaxTotalTokens}."));
                return Finish("I stopped because the agent reached its token budget.", "budget_exceeded", totalInputTokens, totalOutputTokens, totalTokens);
            }

            if (generation.ToolCalls.Count == 0)
            {
                steps.Add(new AgentStep("final_answer", "The model returned a final answer without requesting another tool."));
                return Finish(generation.Text, "success", totalInputTokens, totalOutputTokens, totalTokens);
            }

            var toolOutputs = new List<LlmToolOutput>();
            foreach (var toolCall in generation.ToolCalls)
            {
                if (toolExecutions >= _policy.MaxToolExecutions)
                {
                    steps.Add(new AgentStep("tool_budget_exceeded", $"Stopped after {_policy.MaxToolExecutions} tool executions."));
                    return Finish("I stopped because the agent reached its tool execution budget.", "budget_exceeded", totalInputTokens, totalOutputTokens, totalTokens);
                }

                steps.Add(new AgentStep("tool_requested", $"Model requested '{toolCall.Name}' with arguments {toolCall.Arguments}"));
                if (!_toolRegistry.TryGet(toolCall.Name, out var tool))
                {
                    var error = JsonSerializer.Serialize(new { error = $"Tool '{toolCall.Name}' is not registered or allowed." });
                    steps.Add(new AgentStep("tool_rejected", $"Rejected unregistered tool '{toolCall.Name}'."));
                    spans.Add(new AgentTraceSpan("tool", toolCall.Name, 0, "rejected", 0, 0, 0, 0));
                    toolOutputs.Add(new LlmToolOutput(toolCall.CallId, error));
                    continue;
                }

                toolExecutions++;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var (output, attempts) = await ExecuteToolWithPolicyAsync(tool, toolCall.Arguments, steps, cancellationToken);
                    stopwatch.Stop();
                    spans.Add(new AgentTraceSpan("tool", toolCall.Name, stopwatch.ElapsedMilliseconds, "success", 0, 0, 0, attempts));
                    steps.Add(new AgentStep("tool_executed", $"Executed '{toolCall.Name}' successfully in {stopwatch.ElapsedMilliseconds} ms after {attempts} attempt(s)."));
                    toolOutputs.Add(new LlmToolOutput(toolCall.CallId, output));
                }
                catch (Exception exception) when (exception is ArgumentException or JsonException or TimeoutException)
                {
                    stopwatch.Stop();
                    var stepName = exception is TimeoutException ? "tool_timeout" : "tool_validation_failed";
                    spans.Add(new AgentTraceSpan("tool", toolCall.Name, stopwatch.ElapsedMilliseconds, "failed", 0, 0, 0, _policy.MaxToolRetries + 1));
                    AddToolFailure(toolOutputs, steps, toolCall, exception, stepName);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    spans.Add(new AgentTraceSpan("tool", toolCall.Name, stopwatch.ElapsedMilliseconds, "failed", 0, 0, 0, _policy.MaxToolRetries + 1));
                    AddToolFailure(toolOutputs, steps, toolCall, exception, "tool_failed");
                }
            }

            try
            {
                generation = await CallLlmAsync(
                    "tool_continuation",
                    token => _llmService.ContinueWithToolOutputsAsync(generation.ResponseId, messages, toolOutputs, toolDefinitions, token),
                    spans,
                    ++llmCalls,
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                steps.Add(new AgentStep("llm_timeout", exception.Message));
                return Finish("The model timed out while continuing after a tool call.", "timeout", totalInputTokens, totalOutputTokens, totalTokens);
            }

            totalInputTokens += generation.InputTokens;
            totalOutputTokens += generation.OutputTokens;
            totalTokens += generation.TotalTokens;
        }

        steps.Add(new AgentStep("max_iterations_reached", $"Stopped after {_policy.MaxToolIterations} model/tool iterations."));
        return Finish("I could not complete the request within the allowed agent iterations.", "iteration_limit", totalInputTokens, totalOutputTokens, totalTokens);

        AgentRunResult Finish(string text, string status, int inputTokens, int outputTokens, int tokens)
        {
            runStopwatch.Stop();
            var trace = new AgentTrace(runId, status, runStopwatch.ElapsedMilliseconds, llmCalls, toolExecutions, inputTokens, outputTokens, tokens, spans);
            return new AgentRunResult(text, steps, inputTokens, outputTokens, tokens, trace);
        }
    }

    private async Task<LlmGenerationResult> CallLlmAsync(string name, Func<CancellationToken, Task<LlmGenerationResult>> operation, List<AgentTraceSpan> spans, int callNumber, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await RunWithTimeoutAsync(operation, _policy.LlmTimeoutSeconds, cancellationToken);
            stopwatch.Stop();
            spans.Add(new AgentTraceSpan("llm", $"{name}_{callNumber}", stopwatch.ElapsedMilliseconds, "success", result.InputTokens, result.OutputTokens, result.TotalTokens, 1));
            return result;
        }
        catch
        {
            stopwatch.Stop();
            spans.Add(new AgentTraceSpan("llm", $"{name}_{callNumber}", stopwatch.ElapsedMilliseconds, "failed", 0, 0, 0, 1));
            throw;
        }
    }

    private async Task<(string Output, int Attempts)> ExecuteToolWithPolicyAsync(IAgentTool tool, string argumentsJson, List<AgentStep> steps, CancellationToken cancellationToken)
    {
        var maxAttempts = _policy.MaxToolRetries + 1;
        Exception? lastException = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var output = await RunWithTimeoutAsync(token => tool.ExecuteAsync(argumentsJson, token), _policy.ToolTimeoutSeconds, cancellationToken);
                return (output, attempt);
            }
            catch (ArgumentException) { throw; }
            catch (JsonException) { throw; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt >= maxAttempts) break;
                steps.Add(new AgentStep("tool_retry", $"Retrying '{tool.Name}' after attempt {attempt} failed: {exception.Message}"));
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }
        throw lastException ?? new InvalidOperationException("Tool execution failed after all retry attempts.");
    }

    private static async Task<T> RunWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> operation, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { return await operation(timeoutCts.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation exceeded the {timeoutSeconds}-second timeout.");
        }
    }

    private static void AddToolFailure(List<LlmToolOutput> toolOutputs, List<AgentStep> steps, LlmToolCall toolCall, Exception exception, string stepName)
    {
        var error = JsonSerializer.Serialize(new { error = exception.Message });
        steps.Add(new AgentStep(stepName, $"'{toolCall.Name}' did not execute: {exception.Message}"));
        toolOutputs.Add(new LlmToolOutput(toolCall.CallId, error));
    }
}

public record AgentStep(string Name, string Description);
public record AgentTraceSpan(string Type, string Name, long DurationMs, string Status, int InputTokens, int OutputTokens, int TotalTokens, int Attempts);
public record AgentTrace(string RunId, string Status, long DurationMs, int LlmCalls, int ToolExecutions, int InputTokens, int OutputTokens, int TotalTokens, IReadOnlyList<AgentTraceSpan> Spans);
public record AgentRunResult(string Text, IReadOnlyList<AgentStep> Steps, int InputTokens, int OutputTokens, int TotalTokens, AgentTrace Trace);
