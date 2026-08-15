public interface IRagRetriever
{
    Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int topK = 2,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int topK,
        RagSearchFilter? filter,
        double minimumSimilarity,
        CancellationToken cancellationToken = default);
}
