using System.Collections.Concurrent;
using System.Text.Json;

public sealed class ResilienceSimulationTool : IAgentTool
{
    private readonly ConcurrentDictionary<string, int> _attempts = new();

    public string Name => "simulate_unstable_service";

    public string Description =>
        "Test-only tool for demonstrating agent retry and timeout behavior. " +
        "Use only when the user explicitly asks to simulate a transient service failure, retry behavior, or a slow/timeout scenario.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            scenarioId = new
            {
                type = "string",
                description = "A unique identifier for this simulation run so retry attempts can be tracked."
            },
            failuresBeforeSuccess = new
            {
                type = "integer",
                minimum = 0,
                maximum = 10,
                description = "How many executions should throw a transient HttpRequestException before succeeding."
            },
            delayMs = new
            {
                type = "integer",
                minimum = 0,
                maximum = 15000,
                description = "Artificial delay in milliseconds on every attempt. Use a value above the tool timeout to simulate a timeout."
            }
        },
        required = new[] { "scenarioId", "failuresBeforeSuccess", "delayMs" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        var root = arguments.RootElement;

        if (!root.TryGetProperty("scenarioId", out var scenarioIdElement) ||
            string.IsNullOrWhiteSpace(scenarioIdElement.GetString()))
        {
            throw new ArgumentException("Tool argument 'scenarioId' is required.");
        }

        if (!root.TryGetProperty("failuresBeforeSuccess", out var failuresElement) ||
            !failuresElement.TryGetInt32(out var failuresBeforeSuccess) ||
            failuresBeforeSuccess < 0 || failuresBeforeSuccess > 10)
        {
            throw new ArgumentException("Tool argument 'failuresBeforeSuccess' must be between 0 and 10.");
        }

        if (!root.TryGetProperty("delayMs", out var delayElement) ||
            !delayElement.TryGetInt32(out var delayMs) ||
            delayMs < 0 || delayMs > 15000)
        {
            throw new ArgumentException("Tool argument 'delayMs' must be between 0 and 15000.");
        }

        var scenarioId = scenarioIdElement.GetString()!;
        var attempt = _attempts.AddOrUpdate(scenarioId, 1, (_, current) => current + 1);

        if (delayMs > 0)
            await Task.Delay(delayMs, cancellationToken);

        if (attempt <= failuresBeforeSuccess)
        {
            throw new HttpRequestException(
                $"Simulated transient dependency failure on attempt {attempt}. " +
                $"Configured to fail {failuresBeforeSuccess} time(s) before success.");
        }

        _attempts.TryRemove(scenarioId, out _);

        return JsonSerializer.Serialize(new
        {
            success = true,
            scenarioId,
            attempt,
            failuresBeforeSuccess,
            delayMs,
            message = "The simulated dependency completed successfully."
        });
    }
}
