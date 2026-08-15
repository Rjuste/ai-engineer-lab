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

    public async Task<LlmGenerationResult> GenerateAsync(
        IReadOnlyList<LlmMessage> context,
        CancellationToken cancellationToken = default)
    {
        var instructions = string.Join(
            "\n\n",
            context
                .Where(message => message.Role == "system")
                .Select(message => message.Content));

        var input = context
            .Where(message => message.Role is "user" or "assistant")
            .Select(message => new
            {
                role = message.Role,
                content = message.Content
            })
            .ToArray();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.openai.com/v1/responses");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            instructions,
            input,
            max_output_tokens = 500,
            store = false
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI generation request failed: {(int)response.StatusCode} {json}");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var text = root
            .GetProperty("output")
            .EnumerateArray()
            .Where(item => item.TryGetProperty("content", out _))
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(content =>
                content.TryGetProperty("type", out var type) &&
                type.GetString() == "output_text")
            .Select(content => content.GetProperty("text").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;

        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("input_tokens").GetInt32();
        var outputTokens = usage.GetProperty("output_tokens").GetInt32();
        var totalTokens = usage.GetProperty("total_tokens").GetInt32();

        return new LlmGenerationResult(
            text,
            inputTokens,
            outputTokens,
            totalTokens);
    }
}
