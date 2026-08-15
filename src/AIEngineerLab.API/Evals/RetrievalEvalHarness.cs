using System.Diagnostics;

public sealed record RetrievalEvalCase(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedDocumentPrefixes,
    RagSearchFilter Filter,
    string Category,
    string? PreviousUserMessage = null,
    string? PreviousAssistantMessage = null,
    bool RewriteQuery = false);

public sealed record RetrievalEvalCaseResult(
    string Id,
    string Category,
    string Question,
    IReadOnlyList<string> ExpectedDocumentPrefixes,
    IReadOnlyList<string> RetrievedDocumentIds,
    double RecallAtK,
    double PrecisionAtK,
    double ReciprocalRank,
    long LatencyMs,
    bool Passed);

public sealed record RetrievalEvalReport(
    DateTimeOffset RunAtUtc,
    int K,
    int TotalCases,
    int PassedCases,
    double PassRate,
    double MeanRecallAtK,
    double MeanPrecisionAtK,
    double Mrr,
    double AverageLatencyMs,
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
            "stale-document-trap"),
        new(
            "cross-tenant-security",
            "How much PTO do employees receive?",
            ["pto-us-2026"],
            new RagSearchFilter("tenant-123", "US", 2026, "HR"),
            "authorization-boundary"),
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

            var relevantRetrieved = retrievedIds.Count(id => IsRelevant(id, testCase.ExpectedDocumentPrefixes));
            var recall = testCase.ExpectedDocumentPrefixes.Count == 0
                ? 1
                : (double)relevantRetrieved / testCase.ExpectedDocumentPrefixes.Count;
            var precision = retrievedIds.Count == 0
                ? 0
                : (double)relevantRetrieved / retrievedIds.Count;

            var firstRelevantRank = retrievedIds
                .Select((id, index) => new { id, rank = index + 1 })
                .FirstOrDefault(item => IsRelevant(item.id, testCase.ExpectedDocumentPrefixes))?.rank;
            var reciprocalRank = firstRelevantRank.HasValue ? 1.0 / firstRelevantRank.Value : 0;

            results.Add(new RetrievalEvalCaseResult(
                testCase.Id,
                testCase.Category,
                testCase.Question,
                testCase.ExpectedDocumentPrefixes,
                retrievedIds,
                recall,
                precision,
                reciprocalRank,
                stopwatch.ElapsedMilliseconds,
                recall >= 1));
        }

        return new RetrievalEvalReport(
            DateTimeOffset.UtcNow,
            k,
            results.Count,
            results.Count(result => result.Passed),
            results.Count == 0 ? 0 : (double)results.Count(result => result.Passed) / results.Count,
            Average(results.Select(result => result.RecallAtK)),
            Average(results.Select(result => result.PrecisionAtK)),
            Average(results.Select(result => result.ReciprocalRank)),
            Average(results.Select(result => (double)result.LatencyMs)),
            results);
    }

    private static bool IsRelevant(string documentId, IReadOnlyList<string> expectedPrefixes) =>
        expectedPrefixes.Any(prefix => documentId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static double Average(IEnumerable<double> values)
    {
        var materialized = values.ToList();
        return materialized.Count == 0 ? 0 : materialized.Average();
    }
}
