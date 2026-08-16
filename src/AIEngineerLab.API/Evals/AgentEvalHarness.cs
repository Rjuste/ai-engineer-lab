using System.Text.Json;

public sealed record AgentEvalExpectation(
    string Id,
    string UserRequest,
    string? ExpectedTool,
    IReadOnlyDictionary<string, string>? ExpectedArguments = null,
    IReadOnlyList<string>? ForbiddenTools = null,
    int? MaxToolExecutions = null,
    string? ExpectedFinalStatus = "success");

public sealed record AgentEvalRequest(
    AgentEvalExpectation Expectation,
    AgentRunResult Run);

public sealed record AgentEvalResult(
    string Id,
    bool Passed,
    bool ToolSelectionPassed,
    bool ArgumentsPassed,
    bool ForbiddenToolsPassed,
    bool ExecutionBudgetPassed,
    bool FinalStatusPassed,
    IReadOnlyList<string> RequestedTools,
    IReadOnlyList<string> Violations);

public sealed class AgentEvalHarness
{
    public AgentEvalResult Evaluate(AgentEvalRequest request)
    {
        var expectation = request.Expectation;
        var run = request.Run;
        var violations = new List<string>();
        var requested = ParseRequestedTools(run.Steps);

        var toolSelectionPassed = expectation.ExpectedTool is null
            ? requested.Count == 0
            : requested.Any(x => string.Equals(x.Name, expectation.ExpectedTool, StringComparison.OrdinalIgnoreCase));
        if (!toolSelectionPassed)
            violations.Add(expectation.ExpectedTool is null
                ? $"Expected no tool call, but agent requested: {string.Join(", ", requested.Select(x => x.Name))}."
                : $"Expected tool '{expectation.ExpectedTool}' was not requested.");

        var argumentsPassed = true;
        if (expectation.ExpectedTool is not null && expectation.ExpectedArguments is { Count: > 0 })
        {
            var matchingCall = requested.FirstOrDefault(x => string.Equals(x.Name, expectation.ExpectedTool, StringComparison.OrdinalIgnoreCase));
            argumentsPassed = matchingCall is not null && ArgumentsContain(matchingCall.ArgumentsJson, expectation.ExpectedArguments);
            if (!argumentsPassed)
                violations.Add($"Tool '{expectation.ExpectedTool}' did not receive all expected arguments.");
        }

        var forbidden = expectation.ForbiddenTools ?? Array.Empty<string>();
        var forbiddenHits = requested.Where(x => forbidden.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var forbiddenToolsPassed = forbiddenHits.Length == 0;
        if (!forbiddenToolsPassed)
            violations.Add($"Forbidden tool(s) requested: {string.Join(", ", forbiddenHits)}.");

        var executionBudgetPassed = expectation.MaxToolExecutions is null || run.Trace.ToolExecutions <= expectation.MaxToolExecutions;
        if (!executionBudgetPassed)
            violations.Add($"Tool executions {run.Trace.ToolExecutions} exceeded expected maximum {expectation.MaxToolExecutions}.");

        var finalStatusPassed = expectation.ExpectedFinalStatus is null || string.Equals(run.Trace.Status, expectation.ExpectedFinalStatus, StringComparison.OrdinalIgnoreCase);
        if (!finalStatusPassed)
            violations.Add($"Expected final status '{expectation.ExpectedFinalStatus}', got '{run.Trace.Status}'.");

        return new AgentEvalResult(
            expectation.Id,
            toolSelectionPassed && argumentsPassed && forbiddenToolsPassed && executionBudgetPassed && finalStatusPassed,
            toolSelectionPassed,
            argumentsPassed,
            forbiddenToolsPassed,
            executionBudgetPassed,
            finalStatusPassed,
            requested.Select(x => x.Name).ToArray(),
            violations);
    }

    private static IReadOnlyList<RequestedTool> ParseRequestedTools(IReadOnlyList<AgentStep> steps)
    {
        const string prefix = "Model requested '";
        const string separator = "' with arguments ";
        var result = new List<RequestedTool>();
        foreach (var step in steps.Where(x => x.Name == "tool_requested"))
        {
            if (!step.Description.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var separatorIndex = step.Description.IndexOf(separator, prefix.Length, StringComparison.Ordinal);
            if (separatorIndex < 0) continue;
            var name = step.Description[prefix.Length..separatorIndex];
            var arguments = step.Description[(separatorIndex + separator.Length)..];
            result.Add(new RequestedTool(name, arguments));
        }
        return result;
    }

    private static bool ArgumentsContain(string argumentsJson, IReadOnlyDictionary<string, string> expected)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var pair in expected)
            {
                if (!document.RootElement.TryGetProperty(pair.Key, out var actual)) return false;
                var actualValue = actual.ValueKind == JsonValueKind.String ? actual.GetString() : actual.ToString();
                if (!string.Equals(actualValue, pair.Value, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private sealed record RequestedTool(string Name, string ArgumentsJson);
}
