public class InMemoryVectorStore : IVectorStore
{
    private readonly object _sync = new();
    private readonly List<(RagDocument Document, double[] Embedding)> _items = [];

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    public void Add(RagDocument document, double[] embedding)
    {
        lock (_sync)
        {
            _items.Add((document, embedding));
        }
    }

    public IReadOnlyList<RagSearchResult> Search(double[] queryEmbedding, int topK) =>
        Search(queryEmbedding, topK, filter: null, minimumSimilarity: -1);

    public IReadOnlyList<RagSearchResult> Search(
        double[] queryEmbedding,
        int topK,
        RagSearchFilter? filter,
        double minimumSimilarity = 0)
    {
        List<(RagDocument Document, double[] Embedding)> snapshot;

        lock (_sync)
        {
            snapshot = _items.ToList();
        }

        var eligible = filter is null
            ? snapshot
            : snapshot.Where(item => filter.Matches(item.Document)).ToList();

        return eligible
            .Select(item => new RagSearchResult(
                item.Document,
                CosineSimilarity(queryEmbedding, item.Embedding)))
            .Where(result => result.Score >= minimumSimilarity)
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    public int CountEligible(RagSearchFilter? filter)
    {
        lock (_sync)
        {
            return filter is null
                ? _items.Count
                : _items.Count(item => filter.Matches(item.Document));
        }
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
