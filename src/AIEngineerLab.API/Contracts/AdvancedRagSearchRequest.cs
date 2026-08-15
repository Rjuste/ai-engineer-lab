public sealed record AdvancedRagSearchRequest(
    string Query,
    string? PreviousUserMessage = null,
    string? PreviousAssistantMessage = null,
    bool RewriteQuery = true,
    int CandidateTopK = 20,
    int FinalTopK = 3,
    double MinimumVectorSimilarity = 0,
    double MinimumRerankScore = 0.35,
    string? TenantId = "tenant-123",
    string? Country = "US",
    int? Year = 2026,
    string? Department = null);
