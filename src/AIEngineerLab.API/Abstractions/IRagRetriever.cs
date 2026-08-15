public interface IRagRetriever
{
    Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string query,
        int topK = 2,
        CancellationToken cancellationToken = default);
}
