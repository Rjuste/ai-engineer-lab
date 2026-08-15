public sealed record RagSearchRequest(
    string Query,
    int TopK = 5,
    double MinimumSimilarity = 0,
    string? TenantId = null,
    string? Country = null,
    int? Year = null,
    string? Department = null);
