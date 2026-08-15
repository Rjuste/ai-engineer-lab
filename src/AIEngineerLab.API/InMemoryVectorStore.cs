public class InMemoryVectorStore : IVectorStore
{
    private readonly List<(RagDocument Document, double[] Embedding)> _items = [];

    public int Count => _items.Count;

    public void Add(RagDocument document, double[] embedding)
    {
        _items.Add((document, embedding));
    }

    public IReadOnlyList<RagSearchResult> Search(double[] queryEmbedding, int topK)
    {
        return _items
            .Select(item => new RagSearchResult(
                item.Document,
                CosineSimilarity(queryEmbedding, item.Embedding)))
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    private static double CosineSimilarity(double[] left, double[] right)
    {
        var dotProduct = left.Zip(right, (a, b) => a * b).Sum();
        var leftMagnitude = Math.Sqrt(left.Sum(value => value * value));
        var rightMagnitude = Math.Sqrt(right.Sum(value => value * value));

        if (leftMagnitude == 0 || rightMagnitude == 0)
            return 0;

        return dotProduct / (leftMagnitude * rightMagnitude);
    }
}
