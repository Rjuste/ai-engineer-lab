using System.Text.Json;

public sealed record LlmJudgeRequest(string Question, string Evidence, string Answer);

public sealed record LlmJudgeResult(
    double Correctness,
    double Groundedness,
    double Relevance,
    bool Passed,
    string Reason,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    string RawResponse);

public sealed class LlmGenerationJudge
{
    private readonly ILlmService _llm;

    public LlmGenerationJudge(ILlmService llm) => _llm = llm;

    public async Task<LlmJudgeResult> EvaluateAsync(LlmJudgeRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new List<LlmMessage>
        {
            new("system", """
You are an evaluation judge for a retrieval-augmented generation system.
Evaluate ONLY the candidate answer against the supplied question and evidence.
Do not use outside knowledge. Treat the evidence as the complete source of truth.
Score correctness, groundedness, and relevance from 0.0 to 1.0.
Correctness: whether the answer correctly answers the question according to the evidence.
Groundedness: whether every factual claim in the answer is supported by the evidence.
Relevance: whether the answer directly addresses the question without irrelevant material.
Set passed=true only when correctness >= 0.8, groundedness >= 0.8, and relevance >= 0.8.
Return ONLY valid JSON with this exact shape:
{"correctness":0.0,"groundedness":0.0,"relevance":0.0,"passed":false,"reason":"brief explanation"}
"""),
            new("user", $"QUESTION:\n{request.Question}\n\nEVIDENCE:\n{request.Evidence}\n\nCANDIDATE ANSWER:\n{request.Answer}")
        };

        var generation = await _llm.GenerateAsync(messages, Array.Empty<LlmToolDefinition>(), cancellationToken);
        var raw = generation.Text.Trim();
        var json = StripCodeFence(raw);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var correctness = Clamp(root.GetProperty("correctness").GetDouble());
            var groundedness = Clamp(root.GetProperty("groundedness").GetDouble());
            var relevance = Clamp(root.GetProperty("relevance").GetDouble());
            var calculatedPass = correctness >= 0.8 && groundedness >= 0.8 && relevance >= 0.8;
            var reason = root.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() ?? string.Empty : string.Empty;

            return new LlmJudgeResult(correctness, groundedness, relevance, calculatedPass, reason, generation.InputTokens, generation.OutputTokens, generation.TotalTokens, raw);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException($"LLM judge returned invalid JSON. Raw response: {raw}", exception);
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    private static string StripCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal)) return value;
        var firstNewline = value.IndexOf('\n');
        var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? value[(firstNewline + 1)..lastFence].Trim()
            : value;
    }
}
