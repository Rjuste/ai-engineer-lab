public sealed record GenerationEvalCase(
    string Id,
    string Question,
    string Evidence,
    string Answer,
    IReadOnlyList<string> RequiredFacts,
    IReadOnlyList<string> ForbiddenClaims,
    bool ExpectedPass,
    string Category);

public sealed record GenerationEvalCaseResult(
    string Id,
    string Category,
    string Question,
    string Answer,
    IReadOnlyList<string> MatchedRequiredFacts,
    IReadOnlyList<string> MissingRequiredFacts,
    IReadOnlyList<string> MatchedForbiddenClaims,
    double CorrectnessScore,
    double GroundednessScore,
    double RelevanceScore,
    bool Passed,
    bool ExpectedPass,
    bool ExpectationMatched,
    IReadOnlyList<string> Violations);

public sealed record GenerationEvalReport(
    DateTimeOffset RunAtUtc,
    int TotalCases,
    int PassedCases,
    double PassRate,
    double MeanCorrectness,
    double MeanGroundedness,
    double MeanRelevance,
    int ExpectationMismatches,
    IReadOnlyList<GenerationEvalCaseResult> Cases);

public sealed record GenerationEvalRequest(
    string Question,
    string Evidence,
    string Answer,
    IReadOnlyList<string>? RequiredFacts,
    IReadOnlyList<string>? ForbiddenClaims);

public sealed class GenerationEvalHarness
{
    public IReadOnlyList<GenerationEvalCase> GoldenDataset { get; } =
    [
        new(
            "pto-good",
            "What is the current maximum PTO carryover balance?",
            "US employees receive 15 days of paid time off annually. Unused PTO may carry over into the following calendar year up to a maximum accumulated balance of 25 days.",
            "US employees may carry over unused PTO up to a maximum accumulated balance of 25 days.",
            ["25 days"],
            ["cash out", "sell unused PTO", "30 days"],
            true,
            "grounded-correct"),
        new(
            "pto-hallucination",
            "What is the current maximum PTO carryover balance?",
            "US employees receive 15 days of paid time off annually. Unused PTO may carry over into the following calendar year up to a maximum accumulated balance of 25 days.",
            "Employees may carry over 25 days and can cash out any additional unused PTO.",
            ["25 days"],
            ["cash out"],
            false,
            "unsupported-claim"),
        new(
            "pto-wrong-number",
            "What is the current maximum PTO carryover balance?",
            "US employees receive 15 days of paid time off annually. Unused PTO may carry over into the following calendar year up to a maximum accumulated balance of 25 days.",
            "The maximum PTO carryover balance is 30 days.",
            ["25 days"],
            ["30 days"],
            false,
            "incorrect-answer"),
        new(
            "payment-good",
            "What happens when payment processing times out?",
            "Payment processing can fail when a webhook or downstream payment processor request exceeds its timeout. The system records the failure and the invoice remains unpaid until retry or resolution.",
            "The system records the payment failure and the invoice remains unpaid until the payment is retried or otherwise resolved.",
            ["records the failure", "remains unpaid"],
            ["automatically refunds", "marks the invoice paid"],
            true,
            "grounded-correct"),
        new(
            "payment-unsupported-action",
            "What happens when payment processing times out?",
            "Payment processing can fail when a webhook or downstream payment processor request exceeds its timeout. The system records the failure and the invoice remains unpaid until retry or resolution.",
            "The invoice remains unpaid, and the system automatically refunds the customer.",
            ["remains unpaid"],
            ["automatically refunds"],
            false,
            "unsupported-claim")
    ];

    public GenerationEvalReport RunGoldenDataset()
    {
        var results = GoldenDataset.Select(Evaluate).ToList();
        return BuildReport(results);
    }

    public GenerationEvalCaseResult Evaluate(GenerationEvalRequest request) =>
        Evaluate(new GenerationEvalCase(
            "custom",
            request.Question,
            request.Evidence,
            request.Answer,
            request.RequiredFacts ?? Array.Empty<string>(),
            request.ForbiddenClaims ?? Array.Empty<string>(),
            true,
            "custom"));

    private static GenerationEvalCaseResult Evaluate(GenerationEvalCase testCase)
    {
        var matchedRequired = testCase.RequiredFacts
            .Where(fact => Contains(testCase.Answer, fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missingRequired = testCase.RequiredFacts
            .Where(fact => !matchedRequired.Contains(fact, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var forbidden = testCase.ForbiddenClaims
            .Where(claim => Contains(testCase.Answer, claim))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var correctness = testCase.RequiredFacts.Count == 0
            ? 1
            : (double)matchedRequired.Count / testCase.RequiredFacts.Count;

        var unsupportedTerms = ExtractMeaningfulTerms(testCase.Answer)
            .Where(term => !Contains(testCase.Evidence, term) && !Contains(testCase.Question, term))
            .ToList();
        var answerTerms = ExtractMeaningfulTerms(testCase.Answer);
        var groundedness = answerTerms.Count == 0
            ? 0
            : Math.Clamp(1.0 - ((double)unsupportedTerms.Count / answerTerms.Count), 0, 1);

        var questionTerms = ExtractMeaningfulTerms(testCase.Question);
        var relevantTerms = questionTerms.Count(term => Contains(testCase.Answer, term));
        var relevance = questionTerms.Count == 0 ? 1 : (double)relevantTerms / questionTerms.Count;

        var violations = new List<string>();
        if (missingRequired.Count > 0)
            violations.Add($"Missing required fact(s): {string.Join(", ", missingRequired)}");
        if (forbidden.Count > 0)
            violations.Add($"Contains forbidden/unsupported claim(s): {string.Join(", ", forbidden)}");

        var passed = missingRequired.Count == 0 && forbidden.Count == 0;
        return new GenerationEvalCaseResult(
            testCase.Id,
            testCase.Category,
            testCase.Question,
            testCase.Answer,
            matchedRequired,
            missingRequired,
            forbidden,
            correctness,
            groundedness,
            relevance,
            passed,
            testCase.ExpectedPass,
            passed == testCase.ExpectedPass,
            violations);
    }

    private static GenerationEvalReport BuildReport(IReadOnlyList<GenerationEvalCaseResult> results) =>
        new(
            DateTimeOffset.UtcNow,
            results.Count,
            results.Count(result => result.Passed),
            Ratio(results.Count(result => result.Passed), results.Count),
            Average(results.Select(result => result.CorrectnessScore)),
            Average(results.Select(result => result.GroundednessScore)),
            Average(results.Select(result => result.RelevanceScore)),
            results.Count(result => !result.ExpectationMatched),
            results);

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ExtractMeaningfulTerms(string value)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "to", "of", "in", "is", "are", "was", "were",
            "it", "that", "this", "when", "what", "how", "can", "may", "until", "any", "be"
        };

        return System.Text.RegularExpressions.Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9_-]+")
            .Select(match => match.Value)
            .Where(term => term.Length > 2 && !stopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
    private static double Average(IEnumerable<double> values) { var list = values.ToList(); return list.Count == 0 ? 0 : list.Average(); }
}
