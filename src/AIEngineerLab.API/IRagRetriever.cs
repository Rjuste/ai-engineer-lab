public interface IRagRetriever
{
    IReadOnlyList<RagSearchResult> Search(string query, int topK = 2);
}
