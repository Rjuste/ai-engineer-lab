public interface IVectorStore
{
    int Count { get; }
    void Add(RagDocument document, double[] embedding);
    IReadOnlyList<RagSearchResult> Search(double[] queryEmbedding, int topK);
}
