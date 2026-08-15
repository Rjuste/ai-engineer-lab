public class SimpleEmbeddingService
{
    private static readonly string[][] Concepts =
    [
        ["embedding", "embeddings", "vector", "vectors", "numeric", "mathematical", "meaning", "semantic", "similar", "similarity"],
        ["rag", "retrieval", "retrieve", "retrieved", "knowledge", "external", "context", "generation"],
        ["chunk", "chunks", "chunking", "split", "splits", "passage", "passages", "document", "documents"],
        ["eval", "evals", "evaluation", "evaluations", "quality", "correctness", "groundedness", "latency", "cost", "metrics"]
    ];

    public double[] Embed(string text)
    {
        var terms = Tokenize(text).ToList();
        var vector = new double[Concepts.Length];

        for (var i = 0; i < Concepts.Length; i++)
        {
            vector[i] = terms.Count(term => Concepts[i].Contains(term));
        }

        return Normalize(vector);
    }

    private static double[] Normalize(double[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(value => value * value));

        if (magnitude == 0)
            return vector;

        return vector.Select(value => value / magnitude).ToArray();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var normalized = new string(
            text.ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)
                    ? character
                    : ' ')
                .ToArray());

        return normalized.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
