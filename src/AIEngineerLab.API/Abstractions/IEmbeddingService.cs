public interface IEmbeddingService
{
    Task<double[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
