public interface IRagRetriever
{
    IReadOnlyList<RagDocument> Retrieve(string query, int topK = 2);
}
