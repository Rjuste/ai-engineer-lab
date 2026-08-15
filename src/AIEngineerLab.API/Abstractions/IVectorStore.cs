public interface IVectorStore
{
    int Count { get; }
    void Add(RagDocument document, double[] embedding);

    IReadOnlyList<RagSearchResult> Search(double[] queryEmbedding, int topK);

    IReadOnlyList<RagSearchResult> Search(
        double[] queryEmbedding,
        int topK,
        RagSearchFilter? filter,
        double minimumSimilarity = 0);

    IReadOnlyList<RagSearchResult> KeywordSearch(
        string query,
        int topK,
        RagSearchFilter? filter = null);

    int CountEligible(RagSearchFilter? filter);
}
