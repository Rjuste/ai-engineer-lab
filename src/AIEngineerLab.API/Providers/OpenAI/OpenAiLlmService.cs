using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class OpenAiLlmService : ILlmService
{
    private const string Model = "gpt-5-mini";
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OpenAiLlmService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
    }

    public Task<LlmGenerationResult> GenerateAsync(
        IReadOnlyList<LlmMessage> context,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        var instructions = BuildInstructions(context);
        var input = context
            .Where(message => message.Role is "user" or "assistant")
            .Select(message => new
            {
                role = message.Role,
                content = message.Content
            })
            .ToArray();

        var payload = new
        {
            model = Model,
            instructions,
            input,
            tools = BuildTools(tools),
            tool_choice = "auto",
            max_output_tokens = 500,
            store = true
        };

        return SendAsync(payload, cancellationToken);
    }

    public Task<LlmGenerationResult> ContinueWithToolOutputsAsync(
        string previousResponseId,
        IReadOnlyList<LlmMessage> context,
        IReadOnlyList<LlmToolOutput> toolOutputs,
        IReadOnlyList<LlmToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = Model,
            previous_response_id = previousResponseId,
            instructions = BuildInstructions(context),
            input = toolOutputs.Select(output => new
            {
                type = "function_call_output",
                call_id = output.CallId,
                output = output.Output
            }).ToArray(),
            tools = BuildTools(tools),
            tool_choice = "auto",
            max_output_tokens = 500,
            store = true
        };

        return SendAsync(payload, cancellationToken);
    }

    private async Task<LlmGenerationResult> SendAsync(
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.openai.com/v1/responses");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI generation request failed: {(int)response.StatusCode} {json}");
        }

        using var document = JsonDocument.Parse(json);
        return ParseGeneration(document.RootElement);
    }

    private static string BuildInstructions(IReadOnlyList<LlmMessage> context)
    {
        return string.Join(
            "\n\n",
            context
                .Where(message => message.Role == "system")
                .Select(message => message.Content));
    }

    private static object[] BuildTools(IReadOnlyList<LlmToolDefinition> tools)
    {
        return tools
            .Select(tool => (object)new
            {
                type = "function",
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters,
                strict = true
            })
            .ToArray();
    }

    private static LlmGenerationResult ParseGeneration(JsonElement root)
    {
        var outputItems = root.GetProperty("output").EnumerateArray().ToList();

        var text = outputItems
            .Where(item => item.TryGetProperty("content", out _))
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(content =>
                content.TryGetProperty("type", out var type) &&
                type.GetString() == "output_text")
            .Select(content => content.GetProperty("text").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;

        var toolCalls = outputItems
            .Where(item =>
                item.TryGetProperty("type", out var type) &&
                type.GetString() == "function_call")
            .Select(item => new LlmToolCall(
                item.GetProperty("call_id").GetString() ?? string.Empty,
                item.GetProperty("name").GetString() ?? string.Empty,
                item.GetProperty("arguments").GetString() ?? "{}"))
            .ToList();

        var usage = root.GetProperty("usage");

        return new LlmGenerationResult(
            root.GetProperty("id").GetString() ?? string.Empty,
            text,
            toolCalls,
            usage.GetProperty("input_tokens").GetInt32(),
            usage.GetProperty("output_tokens").GetInt32(),
            usage.GetProperty("total_tokens").GetInt32());
    }
}
