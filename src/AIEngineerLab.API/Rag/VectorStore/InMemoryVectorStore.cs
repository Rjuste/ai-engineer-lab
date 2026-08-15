using System.Text.RegularExpressions;

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
        var eligible = SnapshotEligible(filter);

        return eligible
            .Select(item => new RagSearchResult(
                item.Document,
                CosineSimilarity(queryEmbedding, item.Embedding)))
            .Where(result => result.Score >= minimumSimilarity)
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    public IReadOnlyList<RagSearchResult> KeywordSearch(
        string query,
        int topK,
        RagSearchFilter? filter = null)
    {
        var eligible = SnapshotEligible(filter);

        return eligible
            .Select(item => new RagSearchResult(
                item.Document,
                KeywordScore(query, item.Document.Content)))
            .Where(result => result.Score > 0)
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

    private List<(RagDocument Document, double[] Embedding)> SnapshotEligible(RagSearchFilter? filter)
    {
        lock (_sync)
        {
            return filter is null
                ? _items.ToList()
                : _items.Where(item => filter.Matches(item.Document)).ToList();
        }
    }

    private static double KeywordScore(string query, string content)
    {
        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
            return 0;

        var contentTerms = Tokenize(content);
        var matches = queryTerms.Count(term => contentTerms.Contains(term));
        var coverage = (double)matches / queryTerms.Count;

        // Exact literal matches are especially valuable for IDs, error codes, invoice numbers, etc.
        var exactPhraseBoost = content.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
        var identifierBoost = queryTerms
            .Where(LooksLikeIdentifier)
            .Count(term => contentTerms.Contains(term)) * 0.5;

        return coverage + exactPhraseBoost + identifierBoost;
    }

    private static HashSet<string> Tokenize(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9_-]+")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeIdentifier(string value) =>
        value.Any(char.IsDigit) || value.Contains('_') || value.Contains('-');

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
