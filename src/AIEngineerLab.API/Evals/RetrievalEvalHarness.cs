using System.Diagnostics;

public sealed record RetrievalEvalCase(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedDocumentPrefixes,
    RagSearchFilter Filter,
    string Category,
    string? PreviousUserMessage = null,
    string? PreviousAssistantMessage = null,
    bool RewriteQuery = false,
    IReadOnlyList<string>? ForbiddenDocumentPrefixes = null);

public sealed record RetrievalEvalCaseResult(
    string Id,
    string Category,
    string Question,
    IReadOnlyList<string> ExpectedDocumentPrefixes,
    IReadOnlyList<string> RetrievedDocumentIds,
    IReadOnlyList<string> MatchedExpectedDocumentPrefixes,
    IReadOnlyList<string> ForbiddenRetrievedDocumentIds,
    bool HitAtK,
    double RecallAtK,
    double PrecisionAtK,
    int? FirstRelevantRank,
    double ReciprocalRank,
    long LatencyMs,
    bool Passed,
    IReadOnlyList<string> Violations);

public sealed record RetrievalEvalCategorySummary(
    string Category,
    int TotalCases,
    int PassedCases,
    double PassRate,
    double MeanRecallAtK,
    double MeanPrecisionAtK,
    double Mrr,
    double AverageLatencyMs);

public sealed record RetrievalEvalReport(
    DateTimeOffset RunAtUtc,
    int K,
    int TotalCases,
    int PassedCases,
    double PassRate,
    double HitRateAtK,
    double MeanRecallAtK,
    double MeanPrecisionAtK,
    double Mrr,
    double AverageLatencyMs,
    IReadOnlyList<RetrievalEvalCategorySummary> Categories,
    IReadOnlyList<RetrievalEvalCaseResult> Cases);

public sealed class RetrievalEvalHarness
{
    private readonly AdvancedRagPipeline _pipeline;

    public RetrievalEvalHarness(AdvancedRagPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public IReadOnlyList<RetrievalEvalCase> GoldenDataset { get; } =
    [
        new(
            "err-x92-exact",
            "What caused ERR_X92 in invoice processing?",
            ["incident-err-x92"],
            new RagSearchFilter("tenant-123", "US", 2026, "Engineering"),
            "exact-identifier"),
        new(
            "err-x92-semantic",
            "Why did the invoice payment fail after the webhook took too long?",
            ["incident-err-x92", "payment-timeout-guide"],
            new RagSearchFilter("tenant-123", "US", 2026, "Engineering"),
            "semantic-paraphrase"),
        new(
            "pto-current",
            "What is the current maximum PTO carryover balance?",
            ["pto-us-2026"],
            new RagSearchFilter("tenant-123", "US", 2026, "HR"),
            "current-policy"),
        new(
            "pto-stale-trap",
            "How many PTO days can US employees carry over under the current policy?",
            ["pto-us-2026"],
            new RagSearchFilter("tenant-123", "US", 2026, "HR"),
            "stale-document-trap",
            ForbiddenDocumentPrefixes: ["pto-us-2024"]),
        new(
            "cross-tenant-security",
            "How much PTO do employees receive?",
            ["pto-us-2026"],
            new RagSearchFilter("tenant-123", "US", 2026, "HR"),
            "authorization-boundary",
            ForbiddenDocumentPrefixes: ["pto-other-tenant"]),
        new(
            "contextual-payment-failure",
            "What happens if it doesn't go through?",
            ["payment-timeout-guide"],
            new RagSearchFilter("tenant-123", "US", 2026, "Engineering"),
            "contextual-query",
            "How does invoice payment processing work?",
            "Payments are submitted to the processor and invoice state is updated after the processor response.",
            true)
    ];

    public async Task<RetrievalEvalReport> RunAsync(int k = 5, CancellationToken cancellationToken = default)
    {
        k = Math.Clamp(k, 1, 20);
        var results = new List<RetrievalEvalCaseResult>();

        foreach (var testCase in GoldenDataset)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            var search = await _pipeline.SearchAsync(
                new AdvancedRagSearchRequest(
                    Query: testCase.Question,
                    PreviousUserMessage: testCase.PreviousUserMessage,
                    PreviousAssistantMessage: testCase.PreviousAssistantMessage,
                    RewriteQuery: testCase.RewriteQuery,
                    CandidateTopK: 20,
                    FinalTopK: k,
                    MinimumVectorSimilarity: 0,
                    MinimumRerankScore: 0,
                    TenantId: testCase.Filter.TenantId,
                    Country: testCase.Filter.Country,
                    Year: testCase.Filter.Year,
                    Department: testCase.Filter.Department),
                cancellationToken);
            stopwatch.Stop();

            var retrievedIds = search.FinalResults
                .Take(k)
                .Select(result => result.Document.Id)
                .ToList();

            // Recall is document-level: one expected source may produce multiple chunks,
            // but retrieving two chunks from that source must not count as two expected hits.
            var matchedExpectedPrefixes = testCase.ExpectedDocumentPrefixes
                .Where(prefix => retrievedIds.Any(id => MatchesPrefix(id, prefix)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var relevantRetrieved = retrievedIds.Count(id => IsRelevant(id, testCase.ExpectedDocumentPrefixes));
            var recall = testCase.ExpectedDocumentPrefixes.Count == 0
                ? 1
                : (double)matchedExpectedPrefixes.Count / testCase.ExpectedDocumentPrefixes.Count;
            var precision = retrievedIds.Count == 0
                ? 0
                : (double)relevantRetrieved / retrievedIds.Count;

            var firstRelevantRank = retrievedIds
                .Select((id, index) => new { id, rank = index + 1 })
                .FirstOrDefault(item => IsRelevant(item.id, testCase.ExpectedDocumentPrefixes))?.rank;
            var reciprocalRank = firstRelevantRank.HasValue ? 1.0 / firstRelevantRank.Value : 0;

            var forbiddenPrefixes = testCase.ForbiddenDocumentPrefixes ?? Array.Empty<string>();
            var forbiddenRetrieved = retrievedIds
                .Where(id => IsRelevant(id, forbiddenPrefixes))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var violations = new List<string>();
            if (recall < 1)
            {
                var missing = testCase.ExpectedDocumentPrefixes
                    .Where(prefix => !matchedExpectedPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase));
                violations.Add($"Missing expected source(s): {string.Join(", ", missing)}");
            }

            if (forbiddenRetrieved.Count > 0)
                violations.Add($"Retrieved forbidden source(s): {string.Join(", ", forbiddenRetrieved)}");

            results.Add(new RetrievalEvalCaseResult(
                testCase.Id,
                testCase.Category,
                testCase.Question,
                testCase.ExpectedDocumentPrefixes,
                retrievedIds,
                matchedExpectedPrefixes,
                forbiddenRetrieved,
                firstRelevantRank.HasValue,
                recall,
                precision,
                firstRelevantRank,
                reciprocalRank,
                stopwatch.ElapsedMilliseconds,
                violations.Count == 0,
                violations));
        }

        var categories = results
            .GroupBy(result => result.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RetrievalEvalCategorySummary(
                group.Key,
                group.Count(),
                group.Count(result => result.Passed),
                Ratio(group.Count(result => result.Passed), group.Count()),
                Average(group.Select(result => result.RecallAtK)),
                Average(group.Select(result => result.PrecisionAtK)),
                Average(group.Select(result => result.ReciprocalRank)),
                Average(group.Select(result => (double)result.LatencyMs))))
            .ToList();

        return new RetrievalEvalReport(
            DateTimeOffset.UtcNow,
            k,
            results.Count,
            results.Count(result => result.Passed),
            Ratio(results.Count(result => result.Passed), results.Count),
            Ratio(results.Count(result => result.HitAtK), results.Count),
            Average(results.Select(result => result.RecallAtK)),
            Average(results.Select(result => result.PrecisionAtK)),
            Average(results.Select(result => result.ReciprocalRank)),
            Average(results.Select(result => (double)result.LatencyMs)),
            categories,
            results);
    }

    private static bool IsRelevant(string documentId, IReadOnlyList<string> prefixes) =>
        prefixes.Any(prefix => MatchesPrefix(documentId, prefix));

    private static bool MatchesPrefix(string documentId, string prefix) =>
        documentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : (double)numerator / denominator;

    private static double Average(IEnumerable<double> values)
    {
        var materialized = values.ToList();
        return materialized.Count == 0 ? 0 : materialized.Average();
    }
}
