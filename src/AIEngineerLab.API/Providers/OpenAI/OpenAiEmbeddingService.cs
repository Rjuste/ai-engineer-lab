using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private const string Model = "text-embedding-3-small";
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OpenAiEmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
    }

    public async Task<double[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = JsonSerializer.Serialize(new
        {
            model = Model,
            input = text,
            encoding_format = "float"
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI embeddings request failed: {(int)response.StatusCode} {json}");
        }

        using var document = JsonDocument.Parse(json);
        var embedding = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(value => value.GetDouble())
            .ToArray();

        return embedding;
    }
}
