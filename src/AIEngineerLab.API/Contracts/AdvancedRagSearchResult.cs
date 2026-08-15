public sealed record AdvancedRagCandidate(
    RagDocument Document,
    int? VectorRank,
    double? VectorScore,
    int? KeywordRank,
    double? KeywordScore,
    double RrfScore,
    double RerankScore);

public sealed record AdvancedRagSearchResult(
    string OriginalQuery,
    string SearchQuery,
    bool QueryWasRewritten,
    int RewriteInputTokens,
    int RewriteOutputTokens,
    RagSearchFilter Filter,
    int TotalVectorCount,
    int EligibleVectorCount,
    IReadOnlyList<RagSearchResult> VectorResults,
    IReadOnlyList<RagSearchResult> KeywordResults,
    IReadOnlyList<AdvancedRagCandidate> FusedCandidates,
    IReadOnlyList<AdvancedRagCandidate> FinalResults,
    int CandidateTopK,
    int FinalTopK,
    double MinimumVectorSimilarity,
    double MinimumRerankScore);
