using System.Text.Json;

public sealed class DivisionTool : IAgentTool
{
    public string Name => "divide_numbers";

    public string Description =>
        "Divide one number by another. Use this only when the user explicitly asks for division " +
        "or asks to use the calculator/division tool. The backend rejects a denominator of zero.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            numerator = new
            {
                type = "number",
                description = "The number to divide."
            },
            denominator = new
            {
                type = "number",
                description = "The number to divide by. Must not be zero."
            }
        },
        required = new[] { "numerator", "denominator" },
        additionalProperties = false
    };

    public Task<string> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        var root = arguments.RootElement;

        if (!root.TryGetProperty("numerator", out var numeratorElement) ||
            !numeratorElement.TryGetDouble(out var numerator))
        {
            throw new ArgumentException("Tool argument 'numerator' must be a number.");
        }

        if (!root.TryGetProperty("denominator", out var denominatorElement) ||
            !denominatorElement.TryGetDouble(out var denominator))
        {
            throw new ArgumentException("Tool argument 'denominator' must be a number.");
        }

        if (denominator == 0)
            throw new ArgumentException("Division by zero is not allowed by the backend guard.");

        var result = numerator / denominator;

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            numerator,
            denominator,
            result
        }));
    }
}
